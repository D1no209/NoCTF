using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Persistence;
using Wolverine;

namespace NoCTF.API.Composition;

public sealed class DevelopmentWorkerBootstrapper(
    IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var schedules = await db.DurableMaintenanceSchedules.ToDictionaryAsync(
            schedule => schedule.Kind,
            schedule => schedule.ProcessingVersion,
            cancellationToken);
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var now = DateTimeOffset.UtcNow;
        await bus.SendAsync(new ReconcileRunnerAssignments(
            now,
            schedules[MaintenanceChainKind.RunnerAssignmentReconciliation]));
        await bus.SendAsync(new AdvanceCompetitionLifecycle(
            now,
            schedules[MaintenanceChainKind.CompetitionLifecycle]));
        await bus.SendAsync(new DispatchAwdCheckers(
            now,
            schedules[MaintenanceChainKind.AwdCheckerDispatch]));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
