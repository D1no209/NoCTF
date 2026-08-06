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
using NoCTF.Infrastructure.Administration;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Observability;
using NoCTF.Application.Competitions.Events;
using NoCTF.Worker;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using NoCTF.API.SignalR.Publishing;

var builder = WebApplication.CreateBuilder(args);
var exportSwagger = args.Contains("--export-openapi", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--export-swagger-docs", StringComparer.OrdinalIgnoreCase);
var development = builder.Environment.IsDevelopment() && !exportSwagger;
builder.Configuration["OpenApi:Exporting"] = exportSwagger.ToString();
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = null);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = long.MaxValue);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddNoCtfApi(
    builder.Configuration,
    includeInfrastructure: true,
    development);
builder.Services.AddNoCtfAuthentication(builder.Configuration);
if (development)
{
    builder.Services.AddNoCtfRunner(builder.Configuration, development: true);
}
if (!exportSwagger && !development)
    builder.Services.AddNoCtfPlatformLogging(
        builder.Configuration,
        PlatformLogService.Api);
builder.UseWolverine(options =>
{
    if (development)
    {
        options.Discovery.IncludeType(typeof(BackendMessageHandlers));
        options.Discovery.IncludeType(typeof(CompetitionNotificationMessageHandlers));
        options.Discovery.IncludeType(typeof(DataExportMessageHandlers));
        options.Discovery.IncludeType(typeof(KohPollingHandler));
        options.Discovery.IncludeType(typeof(KohObservationHandler));
        options.Discovery.IncludeType(typeof(LocalCompetitionEventMessageHandler));
        options.Discovery.IncludeAssembly(typeof(RuntimeProviderHandler).Assembly);
        options.StubAllExternalTransports();
    }
    else if (!exportSwagger)
    {
        var postgres = builder.Configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        options.PersistMessagesWithPostgresql(postgres, WolverinePersistenceSchemas.Api);
        options.UseEntityFrameworkCoreTransactions();
        options.PublishMessage<EvaluateSubmission>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<InvalidateLeaderboard>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ProjectLeaderboard>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ApplyCompetitionVisibility>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<CleanupCompetitionRuntimes>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ProvisionCompetitionRuntimes>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<AdvanceCompetitionLifecycle>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<AdvanceAwdRound>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<PollKohChallenge>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DispatchRuntime>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<StopRuntime>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<DrainSubmissions>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<SendEmailVerification>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<SendPasswordReset>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<SendPasswordChangedNotification>()
            .ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<CleanupObject>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ChallengePublished>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<PublishHintNotification>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<TeamBanned>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<TeamBanCorrected>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<CompetitionEventCommitted>()
            .ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<GenerateDataExport>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<ExpireDataExport>().ToPostgresqlQueue("noctf-worker");
        options.PublishMessage<PurgeDataExport>().ToPostgresqlQueue("noctf-worker");
    }
});
if (development)
    builder.Services.AddHostedService<DevelopmentWorkerBootstrapper>();

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
