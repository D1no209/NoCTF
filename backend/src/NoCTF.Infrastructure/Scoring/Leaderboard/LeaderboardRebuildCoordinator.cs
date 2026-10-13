using System.Diagnostics;
using NATS.Client.Core;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

/// <summary>One renewable computation lease per competition; publication still uses its own fence.</summary>
public sealed class LeaderboardRebuildCoordinator
{
    private readonly IClusterLeaseManager leases;
    private readonly TimeProvider clock;

    public LeaderboardRebuildCoordinator(INatsConnection connection, TimeProvider clock)
        : this(new NatsClusterLeaseManager(connection, clock), clock) { }

    internal LeaderboardRebuildCoordinator(IClusterLeaseManager leases, TimeProvider clock)
    {
        this.leases = leases;
        this.clock = clock;
    }

    public async Task<RebuildLease> AcquireAsync(Guid competitionId, CancellationToken ct)
    {
        var key = $"leaderboard-rebuild-{competitionId:N}";
        var owner = Guid.NewGuid().ToString("N");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var lease = await leases.TryAcquireAsync(key, owner, ct);
            if (lease is not null) return new(lease, clock, ct);
            // The process-local keyed lock limits contention to one waiter per process/competition.
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(150, 251)), clock, ct);
        }
    }

    public sealed class RebuildLease : IAsyncDisposable
    {
        private readonly IClusterLease lease;
        private readonly TimeProvider clock;
        private readonly CancellationTokenSource work;
        private readonly CancellationTokenSource stopping;
        private readonly SemaphoreSlim renewalGate = new(1, 1);
        private readonly Task renewal;
        private Exception? failure;

        internal RebuildLease(IClusterLease lease, TimeProvider clock, CancellationToken ct)
        {
            this.lease = lease;
            this.clock = clock;
            work = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
            renewal = RenewAsync();
        }

        public CancellationToken Token => work.Token;

        public void ThrowIfLost()
        {
            if (Volatile.Read(ref failure) is { } cause)
                throw new TimeoutException("Leaderboard computation ownership was lost.", cause);
        }

        public Task ConfirmOwnershipAsync() => RenewOnceAsync(Token);

        private async Task RenewOnceAsync(CancellationToken ct)
        {
            ThrowIfLost();
            await renewalGate.WaitAsync(ct);
            try
            {
                ThrowIfLost();
                await lease.RenewAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref failure, exception, null);
                await work.CancelAsync();
                throw new TimeoutException("Leaderboard computation ownership was lost.", exception);
            }
            finally { renewalGate.Release(); }
        }

        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(NatsClusterLeaseManager.RenewalInterval, clock);
            try
            {
                while (await timer.WaitForNextTickAsync(stopping.Token))
                    await RenewOnceAsync(stopping.Token);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (TimeoutException) { } // The projection token is cancelled and publication checks ownership again.
        }

        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync();
            await renewal;
            var started = Stopwatch.GetTimestamp();
            try { await lease.DisposeAsync(); }
            catch (Exception)
            {
                // The short lease expires if release cannot reach NATS. Never undo a published result.
                NoCtfTelemetry.RecordNatsOperation("leaderboard_rebuild_release", "failure", Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            work.Dispose();
            stopping.Dispose();
            renewalGate.Dispose();
        }
    }
}
