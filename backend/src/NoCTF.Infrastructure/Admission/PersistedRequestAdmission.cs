using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Admission;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Admission;

/// <summary>Atomically acquires all request quotas using provider-neutral EF transactions.</summary>
public sealed class PersistedRequestAdmission(
    IDbContextFactory<NoCtfDbContext> contexts,
    TimeProvider clock) : IRequestAdmission
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private const int MaximumAttempts = 3;

    public async ValueTask<IRequestAdmissionLease> AcquireAsync(
        IReadOnlyList<RateQuota> rates,
        IReadOnlyList<ConcurrentQuota> concurrency,
        CancellationToken ct)
    {
        var rateKeys = rates.Select(item => Key("rate", item.Key)).ToArray();
        var leaseKeys = concurrency.Select(item => Key("lease", item.Key)).ToArray();
        if (rateKeys.Distinct().Count() != rateKeys.Length
            || leaseKeys.Distinct().Count() != leaseKeys.Length
            || rates.Any(item => item.Limit <= 0 || item.WindowSeconds <= 0)
            || concurrency.Any(item => item.Limit <= 0))
            throw new ArgumentException("Request admission quotas must be unique and positive.");

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            try
            {
                return await AcquireOnceAsync(rates, rateKeys, concurrency, leaseKeys, ct);
            }
            catch (AdmissionRejectedException)
            {
                throw;
            }
            catch (Exception exception) when (attempt + 1 < MaximumAttempts
                && (exception is DbUpdateException
                    || TransactionFailureClassifier.IsRetryable(exception)))
            {
                await Task.Delay(Random.Shared.Next(2, 17), ct);
            }
            catch (Exception exception) when (exception is DbException or DbUpdateException
                || TransactionFailureClassifier.IsRetryable(exception))
            {
                throw new AdmissionRejectedException(AdmissionFailure.DependencyUnavailable);
            }
        }
        throw new AdmissionRejectedException(AdmissionFailure.DependencyUnavailable);
    }

    private async Task<IRequestAdmissionLease> AcquireOnceAsync(
        IReadOnlyList<RateQuota> rates, string[] rateKeys,
        IReadOnlyList<ConcurrentQuota> concurrency, string[] leaseKeys,
        CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var now = clock.GetUtcNow();
        var windows = await db.RequestAdmissionWindows
            .Where(item => rateKeys.Contains(item.KeyHash))
            .ToDictionaryAsync(item => item.KeyHash, ct);
        var activeLeases = await db.RequestAdmissionLeases.AsNoTracking()
            .Where(item => leaseKeys.Contains(item.KeyHash)
                && item.ExpiresAt > now)
            .GroupBy(item => item.KeyHash)
            .Select(group => new { KeyHash = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.KeyHash, item => item.Count, ct);

        for (var index = 0; index < rates.Count; index++)
        {
            if (windows.TryGetValue(rateKeys[index], out var window)
                && window.ExpiresAt > now && window.Count >= rates[index].Limit)
                throw new AdmissionRejectedException(AdmissionFailure.RateLimited,
                    Math.Max(1, (int)Math.Ceiling((window.ExpiresAt - now).TotalSeconds)));
        }
        for (var index = 0; index < concurrency.Count; index++)
        {
            if (activeLeases.GetValueOrDefault(leaseKeys[index]) >= concurrency[index].Limit)
                throw new AdmissionRejectedException(AdmissionFailure.CapacityBusy);
        }

        for (var index = 0; index < rates.Count; index++)
        {
            var key = rateKeys[index];
            if (!windows.TryGetValue(key, out var window))
            {
                window = new RequestAdmissionWindow { KeyHash = key };
                db.RequestAdmissionWindows.Add(window);
            }
            if (window.ExpiresAt <= now)
            {
                window.Count = 0;
                window.ExpiresAt = now.AddSeconds(rates[index].WindowSeconds);
            }
            window.Count++;
        }

        var leaseId = Guid.NewGuid();
        foreach (var key in leaseKeys)
            db.RequestAdmissionLeases.Add(new RequestAdmissionLease
            {
                KeyHash = key, LeaseId = leaseId,
                ExpiresAt = now.Add(LeaseDuration)
            });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new Lease(contexts, clock, leaseId, leaseKeys, ct);
    }

    private static string Key(string prefix, string identity) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            prefix + ':' + identity)));

    private sealed class Lease : IRequestAdmissionLease
    {
        private readonly IDbContextFactory<NoCtfDbContext> contexts;
        private readonly TimeProvider clock;
        private readonly Guid id;
        private readonly string[] keys;
        private readonly CancellationTokenSource source;
        private readonly CancellationTokenSource stopping = new();
        private readonly Task renewal;
        public CancellationToken Token => source.Token;

        public Lease(IDbContextFactory<NoCtfDbContext> contexts,
            TimeProvider clock, Guid id, string[] keys, CancellationToken requestToken)
        {
            this.contexts = contexts;
            this.clock = clock;
            this.id = id;
            this.keys = keys;
            source = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
            renewal = keys.Length == 0 ? Task.CompletedTask : RenewAsync();
        }

        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
            try
            {
                while (await timer.WaitForNextTickAsync(stopping.Token))
                {
                    await using var db = await contexts.CreateDbContextAsync(stopping.Token);
                    await using var transaction = await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable, stopping.Token);
                    var now = clock.GetUtcNow();
                    var leases = await db.RequestAdmissionLeases
                        .Where(item => item.LeaseId == id && keys.Contains(item.KeyHash))
                        .ToArrayAsync(stopping.Token);
                    if (leases.Length != keys.Length
                        || leases.Any(item => item.ExpiresAt <= now))
                    {
                        await source.CancelAsync();
                        return;
                    }
                    foreach (var lease in leases)
                        lease.ExpiresAt = now.Add(LeaseDuration);
                    await db.SaveChangesAsync(stopping.Token);
                    await transaction.CommitAsync(stopping.Token);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (Exception)
            {
                await source.CancelAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync();
            await renewal;
            if (keys.Length > 0)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await using var db = await contexts.CreateDbContextAsync(timeout.Token);
                    await db.RequestAdmissionLeases
                        .Where(item => item.LeaseId == id && keys.Contains(item.KeyHash))
                        .ExecuteDeleteAsync(timeout.Token);
                }
                catch (Exception)
                {
                    // Expiry is the release backstop; do not change a committed response.
                }
            }
            source.Dispose();
            stopping.Dispose();
        }
    }
}
