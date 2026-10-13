using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using NSwag.AspNetCore;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints;
using NoCTF.API.OpenApi;
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
var exportOpenApi = args.Contains("--export-openapi", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--export-swagger-docs", StringComparer.OrdinalIgnoreCase);
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
if (args.Contains("--initialize-storage-only", StringComparer.OrdinalIgnoreCase))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    await NoCTF.Hosting.Storage.StorageInitialization.InitializeAsync(builder.Configuration, timeout.Token);
    return;
}
var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);
var mfaRecoveryOnly = builder.Configuration["mfa-recovery-user"] is not null;
var roles = migrateOnly || exportOpenApi || mfaRecoveryOnly
    ? HostRoles.Only(HostRole.Api)
    : HostRoles.FromConfiguration(builder.Configuration);
var development = builder.Environment.IsDevelopment();
builder.Configuration["OpenApi:Exporting"] = exportOpenApi.ToString();
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
        development || exportOpenApi,
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
    if (!development && !exportOpenApi && !generateHandlers)
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
    options.ConfigureNoCtfApiMessaging((development || exportOpenApi) && roles.Has(HostRole.Api));
    if (roles.Has(HostRole.Worker))
        options.ConfigureNoCtfWorkerMessaging(builder.Configuration, durable: !development);
    if (roles.Has(HostRole.Runner))
        options.ConfigureNoCtfRunnerMessaging(builder.Configuration, durable: !development);
    if (development || exportOpenApi)
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
    development || exportOpenApi);
if (!exportOpenApi)
    builder.Services.AddNoCtfObservability(
        builder.Configuration,
        $"noctf-host-{string.Join('-', roles.Values).ToLowerInvariant()}",
        roles);

var app = builder.Build();
if (mfaRecoveryOnly)
{
    await using var recoveryScope = app.Services.CreateAsyncScope();
    await NoCTF.Hosting.Authentication.OfflineMfaRecovery.ExecuteAsync(
        recoveryScope.ServiceProvider.GetRequiredService<NoCTF.Infrastructure.Persistence.NoCtfDbContext>(),
        recoveryScope.ServiceProvider.GetRequiredService<NoCTF.Application.Authentication.Mfa.IMfaManagementStore>(),
        app.Configuration, CancellationToken.None);
    return;
}
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
if (!exportOpenApi)
    app.UseNoCtfObservability();
if (!exportOpenApi && (development || app.Configuration.GetValue("Database:AutoMigrate", false)))
    await DatabaseStartup.InitializeAsync(app.Services, app.Configuration, app.Lifetime.ApplicationStopping);
if (roles.Has(HostRole.Api))
{
    app.UseNoCtfPipeline();
    app.UseNoCtfEndpoints();
    app.MapHub<CompetitionHub>("/hubs/v1/competitions", options => options.CloseOnAuthenticationExpiration = true);
    app.MapHub<NoCTF.API.LiveSolo.Realtime.LiveSoloHub>("/hubs/v1/live-solo", options => options.CloseOnAuthenticationExpiration = true);
    app.MapHub<NotificationHub>("/hubs/v1/notifications", options => options.CloseOnAuthenticationExpiration = true);
    app.MapHub<PlatformLogHub>("/hubs/v1/admin/platform-logs", options => options.CloseOnAuthenticationExpiration = true);
}

app.MapNoCtfHealthChecks();
if (exportOpenApi)
{
    var registration = app.Services
        .GetRequiredService<IEnumerable<OpenApiDocumentRegistration>>()
        .Single(item => item.DocumentName == "v1");
    var descriptions = app.Services
        .GetRequiredService<IApiDescriptionGroupCollectionProvider>();
    await OpenApiExporter.ExportAsync(app, registration, descriptions);
    return;
}
app.Run();

public partial class Program;
