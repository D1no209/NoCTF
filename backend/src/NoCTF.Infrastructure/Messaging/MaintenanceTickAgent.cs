using NoCTF.Application.Messaging;
using Wolverine;
using Wolverine.Runtime.Agents;

namespace NoCTF.Infrastructure.Messaging;

public sealed class MaintenanceTickAgent(IMessageBus bus) : SingularAgent("noctf-maintenance-ticks")
{
    private CancellationTokenSource? stopping;
    private Task? loop;

    protected override Task startAsync(CancellationToken cancellationToken)
    {
        stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        loop = RunAsync(stopping.Token);
        return Task.CompletedTask;
    }

    protected override async Task stopAsync(CancellationToken cancellationToken)
    {
        if (stopping is null || loop is null)
            return;
        await stopping.CancelAsync();
        try
        {
            await loop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            stopping.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var lastSlowTick = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            await bus.PublishAsync(new DispatchAwdCheckers(now));
            if (now - lastSlowTick >= TimeSpan.FromSeconds(30))
            {
                lastSlowTick = now;
                await bus.PublishAsync(new ReconcileRunnerAssignments(now));
                await bus.PublishAsync(new AdvanceCompetitionLifecycle(now));
            }
            await timer.WaitForNextTickAsync(cancellationToken);
        }
    }
}
