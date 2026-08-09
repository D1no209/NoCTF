using Microsoft.AspNetCore.Http.Features;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints;
using NoCTF.API.Security;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Hosting;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Observability;
using NoCTF.Runner;
using NoCTF.Runner.Composition;
using NoCTF.Worker;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
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
var roles = HostRoles.FromConfiguration(builder.Configuration);
var development = builder.Environment.IsDevelopment();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = long.MaxValue);

if (roles.Has(HostRole.Api))
{
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IUserContext, HttpUserContext>();
    builder.Services.AddNoCtfApi(
        builder.Configuration,
        includeInfrastructure: true,
        development,
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
    builder.Services.AddNoCtfWorkerRole();
if (roles.Has(HostRole.Runner))
    builder.Services.AddNoCtfRunner(builder.Configuration, development);
if (!development)
    builder.Services.AddNoCtfPlatformLogging(builder.Configuration, PlatformLogService.Host);

builder.UseWolverine(options =>
{
    options.ConfigureNoCtfApiMessaging(development && roles.Has(HostRole.Api));
    if (roles.Has(HostRole.Worker))
        options.ConfigureNoCtfWorkerMessaging(durable: !development);
    if (roles.Has(HostRole.Runner))
        options.ConfigureNoCtfRunnerMessaging(builder.Configuration, durable: !development);
    if (development)
    {
        options.StubAllExternalTransports();
    }
    else
    {
        options.ConfigureNoCtfPersistence(builder.Configuration, roles);
        options.ConfigureNoCtfMessageRouting(builder.Configuration, roles);
    }
});

var app = builder.Build();
if (development && (roles.Has(HostRole.Api) || roles.Has(HostRole.Worker)))
    await app.Services.InitializeNoCtfAsync();
if (roles.Has(HostRole.Api))
{
    app.UseNoCtfPipeline();
    app.UseNoCtfEndpoints();
    app.MapHub<CompetitionHub>("/hubs/v1/competitions");
    app.MapHub<PlatformLogHub>("/hubs/v1/admin/platform-logs");
}

app.MapGet("/health/live", () => TypedResults.Ok(new
{
    status = "live",
    roles = roles.Values.Select(role => role.ToString())
}));
app.MapGet("/health/ready", () => TypedResults.Ok(new
{
    status = "ready",
    roles = roles.Values.Select(role => role.ToString()),
    runnerPool = roles.Has(HostRole.Runner) ? builder.Configuration["Runner:Pool"] : null,
    runnerId = roles.Has(HostRole.Runner) ? builder.Configuration["Runner:Id"] : null
}));
app.Run();

public partial class Program;
