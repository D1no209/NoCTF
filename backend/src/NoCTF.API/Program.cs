using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Infrastructure;
using FastEndpoints.Swagger;
using NoCTF.Application.Messaging;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
var exportSwagger = args.Contains("--export-openapi", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--export-swagger-docs", StringComparer.OrdinalIgnoreCase);
builder.Configuration["OpenApi:Exporting"] = exportSwagger.ToString();
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = null);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = long.MaxValue);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddNoCtfApi(builder.Configuration, includeInfrastructure: true);
builder.Services.AddNoCtfAuthentication(builder.Configuration);
builder.UseWolverine(options =>
{
    if (!exportSwagger)
    {
        var postgres = builder.Configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        options.PersistMessagesWithPostgresql(postgres, "wolverine");
        options.UseEntityFrameworkCoreTransactions();
        options.PublishMessage<EvaluateSubmission>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ProjectLeaderboard>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<CleanupCompetitionRuntimes>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ProvisionCompetitionRuntimes>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<SendEmailVerification>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DispatchRuntime>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<StopRuntime>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DrainSubmissions>().ToPostgresqlQueue("noctf-worker");
    }
});

var app = builder.Build();
if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
{
    await app.Services.MigrateNoCtfAsync();
    await app.StartAsync();
    await app.StopAsync();
    return;
}
app.UseNoCtfPipeline();
app.UseNoCtfEndpoints();
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
    var json = document.ToJson();
    await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "swagger.json"), json);
    await File.WriteAllTextAsync(Path.Combine(staticDirectory, "v1.json"), json);
    await app.StopAsync();
    return;
}
app.MapHub<CompetitionHub>("/hubs/v1/competitions");
app.Run();

public partial class Program;
