using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Libvirt;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Messaging;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Infrastructure.Messaging;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.ErrorHandling;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNoCtfRunner(builder.Configuration);
var postgres = builder.Configuration.GetConnectionString("PostgreSql")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
builder.Services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
    options => options.UseNpgsql(postgres).UseSnakeCaseNamingConvention());
builder.Services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
var runnerPool = builder.Configuration["Runner:Pool"] ?? "default";
var runnerId = builder.Configuration["Runner:Id"]
    ?? throw new InvalidOperationException("Runner:Id is required.");
var poolQueueName = RunnerQueueName.FromPool(runnerPool);
var nodeQueueName = RunnerNodeQueueName.FromAssignment(runnerPool, runnerId);
builder.UseWolverine(options =>
{
    options.PersistMessagesWithPostgresql(postgres, "wolverine");
    options.UseEntityFrameworkCoreTransactions();
    options.Durability.Mode = DurabilityMode.Balanced;
    options.Policies.OnException<TimeoutException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.Policies.OnException<System.Net.Http.HttpRequestException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.Policies.OnException<Npgsql.NpgsqlException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.ListenToPostgresqlQueue(poolQueueName.Value).UseDurableInbox();
    options.ListenToPostgresqlQueue(nodeQueueName.Value).UseDurableInbox();
    options.PublishMessage<ClaimContainerRuntime>().ToPostgresqlQueue(poolQueueName.Value);
    options.PublishMessage<AwdpFixResult>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<DispatchRuntime>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<AwdFlagInjectionFailed>().ToPostgresqlQueue("noctf-worker");
});
var app = builder.Build();
app.MapGet("/health/live", () => TypedResults.Ok(new { status = "live" }));
app.MapGet("/health/ready", () => TypedResults.Ok(new { status = "ready", runnerPool, runnerId }));
app.Run();

public partial class Program;
