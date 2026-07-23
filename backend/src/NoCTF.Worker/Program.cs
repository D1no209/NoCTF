using NoCTF.Infrastructure;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Messaging;
using Wolverine.ErrorHandling;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNoCtfInfrastructure(builder.Configuration);
builder.UseWolverine(options =>
{
    var postgres = builder.Configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
    options.PersistMessagesWithPostgresql(postgres, "wolverine");
    options.UseEntityFrameworkCoreTransactions();
    options.Durability.Mode = DurabilityMode.Balanced;
    options.Policies.OnException<TimeoutException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.Policies.OnException<System.Net.Http.HttpRequestException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.Policies.OnException<Npgsql.NpgsqlException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    options.Policies.OnException<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>()
        .RetryTimes(5);
    options.ListenToPostgresqlQueue("noctf-worker").UseDurableInbox();
    options.PublishMessage<ReconcileRunnerAssignments>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<ReleaseRunnerCapacity>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<AdvanceCompetitionLifecycle>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<AdvanceAwdRound>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<GenerateAwdFlags>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<AwdFlagInjectionFailed>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<DispatchAwdCheckers>().ToPostgresqlQueue("noctf-worker");
    options.PublishMessage<AwdCheckerCallbackMissing>().ToPostgresqlQueue("noctf-worker");
});

var host = builder.Build();
await host.StartAsync();
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
    var schedules = await db.DurableMaintenanceSchedules.ToDictionaryAsync(
        schedule => schedule.Kind,
        schedule => schedule.ProcessingVersion);
    var bus = host.Services.GetRequiredService<IMessageBus>();
    await bus.SendAsync(new ReconcileRunnerAssignments(
        DateTimeOffset.UtcNow,
        schedules[MaintenanceChainKind.RunnerAssignmentReconciliation]));
    await bus.SendAsync(new AdvanceCompetitionLifecycle(
        DateTimeOffset.UtcNow,
        schedules[MaintenanceChainKind.CompetitionLifecycle]));
    await bus.SendAsync(new DispatchAwdCheckers(
        DateTimeOffset.UtcNow,
        schedules[MaintenanceChainKind.AwdCheckerDispatch]));
}
await host.WaitForShutdownAsync();
