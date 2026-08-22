using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NoCTF.Hosting.Observability;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Observability;
using NoCTF.Runner;
using NoCTF.Runner.Composition;
using NoCTF.Worker;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
var exportSwagger = args.Contains("--export-openapi", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--export-swagger-docs", StringComparer.OrdinalIgnoreCase);
var development = builder.Environment.IsDevelopment() && !exportSwagger;
var roles = development ? HostRoles.All() : HostRoles.Only(HostRole.Api);
builder.Configuration["OpenApi:Exporting"] = exportSwagger.ToString();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = long.MaxValue);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddNoCtfApi(builder.Configuration, includeInfrastructure: true, development);
builder.Services.AddNoCtfAuthentication(builder.Configuration);
if (development)
{
    builder.Services.AddNoCtfWorkerRole(collectQueueMetrics: false);
    builder.Services.AddNoCtfRunner(builder.Configuration, development: true);
}
if (!exportSwagger && !development)
    builder.Services.AddNoCtfPlatformLogging(builder.Configuration, PlatformLogService.Api);

builder.UseWolverine(options =>
{
    options.ConfigureNoCtfApiMessaging(development);
    if (development)
    {
        options.ConfigureNoCtfWorkerMessaging(builder.Configuration, durable: false);
        options.ConfigureNoCtfRunnerMessaging(builder.Configuration, durable: false);
        options.StubAllExternalTransports();
    }
    else if (!exportSwagger)
    {
        options.ConfigureNoCtfPersistence(builder.Configuration, roles);
        options.ConfigureNoCtfMessageRouting(builder.Configuration, roles);
    }
});
builder.Services.AddNoCtfRoleHealthChecks(
    builder.Configuration,
    roles,
    development || exportSwagger);
if (!exportSwagger)
    builder.Services.AddNoCtfObservability(builder.Configuration, "noctf-api");

var app = builder.Build();
if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
{
    await app.Services.InitializeNoCtfAsync();
    await app.StartAsync();
    await app.StopAsync();
    return;
}
if (development)
    await app.Services.InitializeNoCtfAsync();
if (!exportSwagger)
    app.UseNoCtfObservability();
app.UseNoCtfPipeline();
app.UseNoCtfEndpoints();
app.MapNoCtfHealthChecks();
if (exportSwagger)
{
    await app.StartAsync();
    var registration = app.Services.GetRequiredService<IEnumerable<OpenApiDocumentRegistration>>()
        .Single(item => item.DocumentName == "v1");
    var descriptions = app.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
    var generator = new AspNetCoreOpenApiDocumentGenerator(registration.Settings);
    var document = await generator.GenerateAsync(descriptions.ApiDescriptionGroups);
    var repositoryRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", ".."));
    var artifactDirectory = Path.Combine(repositoryRoot, "artifacts", "openapi");
    var staticDirectory = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "openapi");
    Directory.CreateDirectory(artifactDirectory);
    Directory.CreateDirectory(staticDirectory);
    var json = document.ToJson().ReplaceLineEndings("\n").TrimEnd() + "\n";
    await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "swagger.json"), json);
    await File.WriteAllTextAsync(Path.Combine(staticDirectory, "v1.json"), json);
    await app.StopAsync();
    return;
}
app.MapHub<CompetitionHub>("/hubs/v1/competitions");
app.MapHub<PlatformLogHub>("/hubs/v1/admin/platform-logs");
app.Run();

public partial class Program;
