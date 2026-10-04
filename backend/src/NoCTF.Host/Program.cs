using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Http.Features;
using FastEndpoints.OpenApi.Kiota;
using Kiota.Builder;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints;
using NoCTF.API.Security;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NoCTF.Hosting.Observability;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using NoCTF.Runner;
using NoCTF.Runner.Composition;
using NoCTF.Worker;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
var generateHandlers = args.Contains("codegen", StringComparer.OrdinalIgnoreCase);
var apiConfigurationRoot = Path.GetFullPath(Path.Combine(
    builder.Environment.ContentRootPath,
    "..",
    "NoCTF.API"));
if (Directory.Exists(apiConfigurationRoot))
{
    builder.Configuration
        .AddJsonFile(
            Path.Combine(apiConfigurationRoot, "appsettings.json"),
            optional: false,
            reloadOnChange: true)
        .AddJsonFile(
            Path.Combine(
                apiConfigurationRoot,
                $"appsettings.{builder.Environment.EnvironmentName}.json"),
            optional: true,
            reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args);
}
var generationMode = !builder.IsNotGenerationMode();
if (builder.IsApiClientGenerationMode() && builder.IsOpenApiJsonExportMode())
    throw new ArgumentException("Generate clients and export OpenAPI in separate processes.");
if (generationMode && (generateHandlers
    || args.Contains("--initialize-storage-only", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase)))
    throw new ArgumentException("API generation cannot be combined with storage initialization, migrations, or handler generation.");
if (args.Contains("--initialize-storage-only", StringComparer.OrdinalIgnoreCase))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    await NoCTF.Hosting.Storage.StorageInitialization.InitializeAsync(builder.Configuration, timeout.Token);
    return;
}
if (generationMode)
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Configuration["Authentication:SigningKey"] = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    builder.Configuration["RunnerScoring:SigningKey"] = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);
var roles = migrateOnly || generationMode
    ? HostRoles.Only(HostRole.Api)
    : HostRoles.FromConfiguration(builder.Configuration);
var development = builder.Environment.IsDevelopment();
builder.Configuration["OpenApi:Generating"] = generationMode.ToString();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = long.MaxValue);
builder.Services.AddNoCtfDatabaseProvider(builder.Configuration);

if (roles.Has(HostRole.Api))
{
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IUserContext, HttpUserContext>();
    builder.Services.AddNoCtfApi(
        builder.Configuration,
        includeInfrastructure: true,
        development || generationMode,
        endpointAssemblies: [typeof(HealthEndpoint).Assembly]);
    builder.Services.AddNoCtfAuthentication(builder.Configuration);
}
else if (roles.Has(HostRole.Worker))
{
    builder.Services.AddNoCtfInfrastructure(builder.Configuration, development);
}
else
{
    builder.Services.AddNoCtfStandaloneRunnerPersistence(builder.Configuration);
}

if (roles.Has(HostRole.Worker))
    builder.Services.AddNoCtfWorkerRole(
        builder.Configuration,
        collectQueueMetrics: !development && builder.Configuration.GetValue("Observability:Enabled", true),
        validateMessageTopology: !development,
        enableClusterScheduling: !development);
if (roles.Has(HostRole.Runner))
    builder.Services.AddNoCtfRunner(builder.Configuration, development);
builder.UseWolverine(options =>
{
    options.ServiceLocationPolicy = JasperFx.CodeGeneration.Model.ServiceLocationPolicy.NotAllowed;
    if (!development && !generationMode && !generateHandlers)
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
    options.ConfigureNoCtfApiMessaging((development || generationMode) && roles.Has(HostRole.Api));
    if (roles.Has(HostRole.Worker))
        options.ConfigureNoCtfWorkerMessaging(builder.Configuration, durable: !development);
    if (roles.Has(HostRole.Runner))
        options.ConfigureNoCtfRunnerMessaging(builder.Configuration, durable: !development);
    if (development || generationMode)
    {
        options.StubAllExternalTransports();
    }
    else
    {
        options.ConfigureNoCtfPersistence(builder.Configuration, roles);
        options.ConfigureNoCtfMessageRouting(builder.Configuration, roles);
        if (generateHandlers)
            options.StubAllExternalTransports();
    }
});
builder.Services.AddNoCtfDatabaseStartup(builder.Configuration);
builder.Services.AddNoCtfRoleHealthChecks(
    builder.Configuration,
    roles,
    development || generationMode);
if (!generationMode)
    builder.Services.AddNoCtfObservability(
        builder.Configuration,
        $"noctf-host-{string.Join('-', roles.Values).ToLowerInvariant()}");

var app = builder.Build();
if (generateHandlers)
{
    await app.RunJasperFxCommands(args);
    return;
}
if (migrateOnly)
{
    await DatabaseStartup.InitializeAsync(app.Services, app.Configuration, app.Lifetime.ApplicationStopping);
    return;
}
if (!generationMode)
    app.UseNoCtfObservability();
if (!generationMode && (development || app.Configuration.GetValue("Database:AutoMigrate", false)))
    await DatabaseStartup.InitializeAsync(app.Services, app.Configuration, app.Lifetime.ApplicationStopping);
if (roles.Has(HostRole.Api))
{
    app.UseNoCtfPipeline();
    app.UseNoCtfEndpoints();
    app.MapHub<CompetitionHub>("/hubs/v1/competitions");
    app.MapHub<NotificationHub>("/hubs/v1/notifications");
    app.MapHub<PlatformLogHub>("/hubs/v1/admin/platform-logs");
}

app.MapNoCtfHealthChecks();
if (generationMode)
{
    var backendRoot = Path.GetFullPath(Path.Combine(apiConfigurationRoot, "..", ".."));
    await app.ExportOpenApiJsonAndExitAsync("v1", Path.Combine(backendRoot, "artifacts", "openapi"));
    await app.GenerateApiClientsAndExitAsync(config =>
    {
        config.OpenApiDocumentName = "v1";
        config.Language = GenerationLanguage.TypeScript;
        config.ClientClassName = "NoCtfClient";
        config.ClientNamespaceName = "NoCTF";
        config.OutputPath = Path.Combine(apiConfigurationRoot, "ClientApp", "app", "api");
        config.CleanOutput = true;
        config.CreateZipArchive = false;
    });
    return;
}
app.Run();

public partial class Program;
