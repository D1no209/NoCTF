using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using k8s;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.PluginBase;
using NoCTF.Runner;

var builder = WebApplication.CreateBuilder(args);

const string MutationPolicy = "runner-mutations";
var maximumRequestBodyBytes = Math.Clamp(
    builder.Configuration.GetValue<long>("Runner:MaxRequestBodyBytes", 4 * 1024 * 1024),
    64 * 1024,
    16 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maximumRequestBodyBytes;
});
var concurrentOperations = Math.Clamp(
    builder.Configuration.GetValue("Runner:Concurrency:PermitLimit", 8),
    1,
    256);
var queuedOperations = Math.Clamp(
    builder.Configuration.GetValue("Runner:Concurrency:QueueLimit", 64),
    0,
    4_096);
var operationReceiptRetention = TimeSpan.FromMinutes(Math.Clamp(
    builder.Configuration.GetValue("Runner:OperationReceiptMinutes", 30),
    1,
    1_440));
var maxOperationDuration = TimeSpan.FromSeconds(Math.Clamp(
    builder.Configuration.GetValue("Runner:MaxOperationSeconds", 1_800),
    30,
    7_200));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddConcurrencyLimiter(MutationPolicy, limiter =>
    {
        limiter.PermitLimit = concurrentOperations;
        limiter.QueueLimit = queuedOperations;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});
builder.Services.AddSingleton(services => new RunnerOperationCoordinator(
    retention: operationReceiptRetention,
    maxEntries: Math.Clamp(
        builder.Configuration.GetValue("Runner:OperationReceiptLimit", 512),
        128,
        65_536),
    maxConcurrentOperations: concurrentOperations,
    maxQueuedOperations: queuedOperations,
    operationCancellationToken: services
        .GetRequiredService<IHostApplicationLifetime>()
        .ApplicationStopping,
    maxOperationDuration: maxOperationDuration));

var runnerProvider = builder.Configuration["Runner:Provider"] ?? "Docker";
if (runnerProvider.Equals("Kubernetes", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton(_ => new NoCTF.Container.K8s.KubernetesProvider(builder.Configuration));
    builder.Services.AddScoped<NoCTF.Container.K8s.KubernetesManager>();
    builder.Services.AddScoped<IContainerManager>(services =>
        services.GetRequiredService<NoCTF.Container.K8s.KubernetesManager>());
    builder.Services.AddHostedService<KubernetesRunReceiptCleanupService>();
}
else
{
    builder.Services.AddSingleton(
        _ => new NoCTF.Container.Docker.DockerProvider(
            builder.Configuration["Docker:Host"],
            builder.Configuration["Docker:PublishedHost"],
            operationReceiptRetention,
            TimeSpan.FromHours(Math.Clamp(
                builder.Configuration.GetValue("Docker:RuntimeOrphanGraceHours", 168),
                1,
                720))));
    builder.Services.AddHostedService<DockerRunReceiptCleanupService>();
    builder.Services.AddScoped<IContainerManager, NoCTF.Container.Docker.DockerManager>();
}

var allowedRegistries = RunnerImagePolicy.ReadAllowedRegistries(builder.Configuration);
var allowedNetworks = RunnerImagePolicy.ReadAllowedNetworks(builder.Configuration);
if (!builder.Environment.IsDevelopment() && allowedRegistries.Length == 0)
    throw new InvalidOperationException("Runner:AllowedRegistries must be configured outside Development.");
var runnerApiKey = builder.Configuration["Runner:ApiKey"];
var requireRunnerAuth = !builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(runnerApiKey);
if (requireRunnerAuth && string.IsNullOrWhiteSpace(runnerApiKey))
    throw new InvalidOperationException("Runner:ApiKey must be configured outside Development.");
if (!builder.Environment.IsDevelopment())
    SecretValueValidator.RequireSafe("Runner:ApiKey", runnerApiKey, 24);

var app = builder.Build();
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (RunnerOperationRejectedException)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(
            new { code = "runner_overloaded" },
            CancellationToken.None);
    }
    catch (RunnerOperationConflictException)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(
            new { code = "operation_id_conflict" },
            CancellationToken.None);
    }
    catch (RunnerOperationTimeoutException)
    {
        context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        await context.Response.WriteAsJsonAsync(
            new { code = "operation_timeout" },
            CancellationToken.None);
    }
});
app.UseRateLimiter();

app.MapGet("/runner/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/runner/health/ready", CheckRunnerReadinessAsync);
app.MapGet("/runner/health", CheckRunnerReadinessAsync);

app.MapGet("/runner/info", (HttpRequest request, IServiceProvider services) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    var info = new Dictionary<string, object?>
    {
        ["provider"] = runnerProvider,
        ["allowedRegistries"] = allowedRegistries,
        ["allowedNetworks"] = allowedNetworks,
    };
    if (services.GetService<NoCTF.Container.K8s.KubernetesProvider>() is { } k8sProvider)
    {
        info["kubernetes"] = new
        {
            connectionMode = k8sProvider.ConnectionMode,
            publicEntry = k8sProvider.Options.PublicEntry,
            defaultExposure = k8sProvider.Options.DefaultExposure,
            ingressBaseDomain = k8sProvider.Options.IngressBaseDomain,
            namespacePrefix = k8sProvider.Options.NamespacePrefix,
            registryCount = k8sProvider.Options.Registries.Count,
            imagePullSecrets = k8sProvider.Options.ImagePullSecrets,
            quota = new
            {
                cpu = k8sProvider.Options.NamespaceCpuLimit,
                memory = k8sProvider.Options.NamespaceMemoryLimit,
                pods = k8sProvider.Options.NamespacePodLimit
            }
        };
    }
    return Results.Ok(info);
});

app.MapPost("/runner/containers", async (
    HttpRequest request,
    ContainerConfig config,
    IContainerManager manager,
    RunnerOperationCoordinator operations,
    CancellationToken ct) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    try
    {
        RunnerImagePolicy.ValidateContainerConfig(config, allowedNetworks);
        var disallowed = RunnerImagePolicy.FindDisallowedContainerImage(config, allowedRegistries);
        if (!string.IsNullOrWhiteSpace(disallowed))
            return Results.BadRequest(new { code = "image_registry_not_allowed", image = disallowed });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { code = "invalid_container_config", message = ex.Message });
    }

    var result = await operations.ExecuteAsync(
        "container.create",
        config.OperationId,
        ComputeRequestFingerprint(config),
        token => manager.CreateContainerAsync(config, token),
        ct);
    return Results.Ok(result);
});

app.MapPost("/runner/containers/destroy", async (
    HttpRequest request,
    ContainerInstance container,
    IContainerManager manager,
    CancellationToken ct) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    await manager.DestroyContainerAsync(container, ct);
    return Results.NoContent();
}).RequireRateLimiting(MutationPolicy);

app.MapPost("/runner/jobs/one-shot", async (
    HttpRequest request,
    ContainerConfig config,
    IContainerManager manager,
    RunnerOperationCoordinator operations,
    CancellationToken ct) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    try
    {
        RunnerImagePolicy.ValidateContainerConfig(config, allowedNetworks);
        var disallowed = RunnerImagePolicy.FindDisallowedContainerImage(config, allowedRegistries);
        if (!string.IsNullOrWhiteSpace(disallowed))
            return Results.BadRequest(new { code = "image_registry_not_allowed", image = disallowed });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { code = "invalid_container_config", message = ex.Message });
    }

    var result = await operations.ExecuteAsync(
        "container.run",
        config.OperationId,
        ComputeRequestFingerprint(config),
        token => manager.RunContainerAsync(config, token),
        ct);
    return Results.Ok(result);
});

app.MapPost("/runner/compose/up", async (
    HttpRequest request,
    ComposeConfig config,
    IContainerManager manager,
    RunnerOperationCoordinator operations,
    CancellationToken ct) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    try
    {
        var disallowed = RunnerImagePolicy.FindDisallowedComposeImage(config.ComposeYaml, allowedRegistries);
        if (!string.IsNullOrWhiteSpace(disallowed))
            return Results.BadRequest(new { code = "image_registry_not_allowed", image = disallowed });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { code = "invalid_compose_yaml", message = ex.Message });
    }

    var result = await operations.ExecuteAsync(
        "compose.up",
        config.OperationId,
        ComputeRequestFingerprint(config),
        token => manager.ComposeUpAsync(config, token),
        ct);
    return Results.Ok(result);
});

app.MapPost("/runner/compose/down", async (
    HttpRequest request,
    ComposeDeployment deployment,
    IContainerManager manager,
    CancellationToken ct) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    await manager.ComposeDownAsync(deployment, ct);
    return Results.NoContent();
}).RequireRateLimiting(MutationPolicy);

app.MapGet("/runner/compose/{projectName}/status", async (
    HttpRequest request,
    string projectName,
    IContainerManager manager,
    CancellationToken ct) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    var labels = request.Query["label"]
        .Select(value => value?.Split('=', 2))
        .Where(parts => parts is { Length: 2 } && !string.IsNullOrWhiteSpace(parts[0]))
        .ToDictionary(parts => parts![0], parts => parts![1], StringComparer.Ordinal);

    return Results.Ok(await manager.GetComposeStatusAsync(projectName, labels, ct));
});

await app.RunAsync();

static async Task<IResult> CheckRunnerReadinessAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        if (services.GetService<NoCTF.Container.Docker.DockerProvider>() is { } dockerProvider)
            await dockerProvider.CreateClient().System.GetVersionAsync(timeout.Token);
        else if (services.GetService<NoCTF.Container.K8s.KubernetesProvider>() is { } kubernetesProvider)
            await kubernetesProvider.Client.Version.GetCodeAsync(timeout.Token);
        else
            return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);

        return Results.Ok(new { status = "healthy" });
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

static IResult? RequireRunnerAuth(HttpRequest request, string? apiKey, bool required)
{
    if (!required)
        return null;

    var provided = request.Headers["X-Runner-Token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(provided) ||
        string.IsNullOrWhiteSpace(apiKey) ||
        !FixedTimeEquals(provided, apiKey))
        return Results.Unauthorized();

    return null;
}

static bool FixedTimeEquals(string provided, string expected)
{
    var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
    var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
}

static string ComputeRequestFingerprint<TRequest>(TRequest request)
    => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
