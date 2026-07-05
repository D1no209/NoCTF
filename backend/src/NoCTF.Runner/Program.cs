using NoCTF.PluginBase;
using NoCTF.Runner;

var builder = WebApplication.CreateBuilder(args);

var runnerProvider = builder.Configuration["Runner:Provider"] ?? "Docker";
if (runnerProvider.Equals("Kubernetes", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton(_ => new NoCTF.Container.K8s.KubernetesProvider(builder.Configuration));
    builder.Services.AddScoped<IContainerManager, NoCTF.Container.K8s.KubernetesManager>();
}
else
{
    builder.Services.AddSingleton(
        _ => new NoCTF.Container.Docker.DockerProvider(builder.Configuration["Docker:Host"]));
    builder.Services.AddScoped<IContainerManager, NoCTF.Container.Docker.DockerManager>();
}

var allowedRegistries = RunnerImagePolicy.ReadAllowedRegistries(builder.Configuration);
if (!builder.Environment.IsDevelopment() && allowedRegistries.Length == 0)
    throw new InvalidOperationException("Runner:AllowedRegistries must be configured outside Development.");
var runnerApiKey = builder.Configuration["Runner:ApiKey"];
var requireRunnerAuth = !builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(runnerApiKey);
if (requireRunnerAuth && string.IsNullOrWhiteSpace(runnerApiKey))
    throw new InvalidOperationException("Runner:ApiKey must be configured outside Development.");

var app = builder.Build();

app.MapGet("/runner/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/runner/info", (HttpRequest request, IServiceProvider services) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    var info = new Dictionary<string, object?>
    {
        ["provider"] = runnerProvider,
        ["allowedRegistries"] = allowedRegistries,
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
    CancellationToken ct) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    try
    {
        RunnerImagePolicy.ValidateContainerConfig(config);
        var disallowed = RunnerImagePolicy.FindDisallowedContainerImage(config, allowedRegistries);
        if (!string.IsNullOrWhiteSpace(disallowed))
            return Results.BadRequest(new { code = "image_registry_not_allowed", image = disallowed });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { code = "invalid_container_config", message = ex.Message });
    }

    return Results.Ok(await manager.CreateContainerAsync(config, ct));
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
});

app.MapPost("/runner/jobs/one-shot", async (
    HttpRequest request,
    ContainerConfig config,
    IContainerManager manager,
    CancellationToken ct) =>
{
    if (RequireRunnerAuth(request, runnerApiKey, requireRunnerAuth) is { } authFailure)
        return authFailure;

    try
    {
        RunnerImagePolicy.ValidateContainerConfig(config);
        var disallowed = RunnerImagePolicy.FindDisallowedContainerImage(config, allowedRegistries);
        if (!string.IsNullOrWhiteSpace(disallowed))
            return Results.BadRequest(new { code = "image_registry_not_allowed", image = disallowed });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { code = "invalid_container_config", message = ex.Message });
    }

    return Results.Ok(await manager.RunContainerAsync(config, ct));
});

app.MapPost("/runner/compose/up", async (
    HttpRequest request,
    ComposeConfig config,
    IContainerManager manager,
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

    return Results.Ok(await manager.ComposeUpAsync(config, ct));
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
});

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

static IResult? RequireRunnerAuth(HttpRequest request, string? apiKey, bool required)
{
    if (!required)
        return null;

    var provided = request.Headers["X-Runner-Token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(provided) || !string.Equals(provided, apiKey, StringComparison.Ordinal))
        return Results.Unauthorized();

    return null;
}
