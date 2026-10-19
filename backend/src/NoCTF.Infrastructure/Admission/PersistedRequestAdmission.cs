using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Admission;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Admission;

/// <summary>Atomically acquires all request quotas using provider-neutral EF transactions.</summary>
public sealed class PersistedRequestAdmission(
    IDbContextFactory<NoCtfDbContext> contexts,
    TimeProvider clock) : IRequestAdmission
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private const int MaximumAttempts = 3;

    public async Task WarmupAsync(CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        string[] keys = [Key("warmup", "query-shape")];
        await ReadWindowsAsync(db, keys, ct);
        await ReadLeasesAsync(db, keys, clock.GetUtcNow(), ct);
    }

    private static Task<Dictionary<string, RequestAdmissionWindow>> ReadWindowsAsync(NoCtfDbContext db, string[] keys, CancellationToken ct) =>
        db.RequestAdmissionWindows.Where(item => keys.Contains(item.KeyHash)).ToDictionaryAsync(item => item.KeyHash, ct);

    private static Task<Dictionary<string, int>> ReadLeasesAsync(NoCtfDbContext db, string[] keys, DateTimeOffset now, CancellationToken ct) =>
        db.RequestAdmissionLeases.AsNoTracking().Where(item => keys.Contains(item.KeyHash) && item.ExpiresAt > now)
            .GroupBy(item => item.KeyHash).Select(group => new { KeyHash = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.KeyHash, item => item.Count, ct);

    public async ValueTask<IRequestAdmissionLease> AcquireAsync(
        IReadOnlyList<RateQuota> rates,
        IReadOnlyList<ConcurrentQuota> concurrency,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (rates.Count == 0 && concurrency.Count == 0)
            return new UnrestrictedLease(ct);
        rates = rates.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        concurrency = concurrency.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
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
                // Rate-only admission (login, SSO, sensitive entries) is the burst path. Atomic conditional
                // updates replace the serializable read-check-write, so a shared NAT address does not turn
                // one window row into a serialization-failure hotspot.
                if (concurrency.Count == 0)
                {
                    await AcquireRatesAsync(rates, rateKeys, ct);
                    return new UnrestrictedLease(ct);
                }
                return await AcquireOnceAsync(rates, rateKeys, concurrency, leaseKeys, ct);
            }
            catch (AdmissionRejectedException)
            {
                throw;
            }
            catch (Exception exception) when (attempt + 1 < MaximumAttempts
                && IsAdmissionConflict(exception))
            {
                NoCtfTelemetry.RecordAdmissionTransaction(AdmissionDiagnosticOutcome.Retry, ClassifyFailure(exception));
            }
            catch (Exception exception) when (TransactionFailureClassifier.IsDependencyUnavailable(exception)
                || IsAdmissionConflict(exception))
            {
                NoCtfTelemetry.RecordAdmissionTransaction(AdmissionDiagnosticOutcome.Exhausted, ClassifyFailure(exception));
                if (IsAdmissionConflict(exception)) throw;
                throw new AdmissionRejectedException(AdmissionFailure.DependencyUnavailable);
            }
            await Task.Delay(Random.Shared.Next(5, 51) * (attempt + 1), ct);
        }
        throw new AdmissionRejectedException(AdmissionFailure.DependencyUnavailable);
    }

    private async Task AcquireRatesAsync(IReadOnlyList<RateQuota> rates, string[] rateKeys, CancellationToken ct)
    {
        using var measurement = NoCtfTelemetry.MeasureAdmission(AdmissionPerformanceStage.AcquireAttempt);
        await using var db = await contexts.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        for (var index = 0; index < rates.Count; index++)
        {
            var retryAfter = await TryConsumeAsync(db, rateKeys[index], rates[index], now, ct);
            if (retryAfter is not { } seconds) continue;
            // All-or-nothing: return the units already taken from earlier windows.
            for (var previous = 0; previous < index; previous++)
            {
                var key = rateKeys[previous];
                await db.RequestAdmissionWindows.Where(item => item.KeyHash == key && item.Count > 0)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Count, item => item.Count - 1)
                        .SetProperty(item => item.ConcurrencyStamp, Guid.NewGuid()), CancellationToken.None);
            }
            throw new AdmissionRejectedException(AdmissionFailure.RateLimited, seconds);
        }
    }

    /// <summary>Consumes one unit, returning null on success or the retry-after seconds when full.</summary>
    private static async Task<int?> TryConsumeAsync(NoCtfDbContext db, string key, RateQuota rate,
        DateTimeOffset now, CancellationToken ct)
    {
        var limit = rate.Limit;
        var expiresAt = now.AddSeconds(rate.WindowSeconds);
        // Each statement is atomic on the window row. A competing request can only move the row between
        // states (missing -> open -> full/expired), so a few local rounds always reach a decision.
        for (var round = 0; round < 4; round++)
        {
            if (await db.RequestAdmissionWindows
                    .Where(item => item.KeyHash == key && item.ExpiresAt > now && item.Count < limit)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Count, item => item.Count + 1)
                        .SetProperty(item => item.ConcurrencyStamp, Guid.NewGuid()), ct) == 1)
                return null;
            if (await db.RequestAdmissionWindows
                    .Where(item => item.KeyHash == key && item.ExpiresAt <= now)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Count, 1)
                        .SetProperty(item => item.ExpiresAt, expiresAt)
                        .SetProperty(item => item.ConcurrencyStamp, Guid.NewGuid()), ct) == 1)
                return null;
            var current = await db.RequestAdmissionWindows.AsNoTracking()
                .Where(item => item.KeyHash == key)
                .Select(item => new { item.ExpiresAt, item.Count })
                .SingleOrDefaultAsync(ct);
            if (current is not null)
            {
                if (current.ExpiresAt > now && current.Count >= limit)
                    return Math.Max(1, (int)Math.Ceiling((current.ExpiresAt - now).TotalSeconds));
                continue; // Rolled over or decremented by another request meanwhile.
            }
            db.RequestAdmissionWindows.Add(new RequestAdmissionWindow { KeyHash = key, Count = 1, ExpiresAt = expiresAt });
            try
            {
                await db.SaveChangesAsync(ct);
                return null;
            }
            catch (DbUpdateException exception) when (exception.InnerException is DbException { SqlState: "23505" })
            {
                // Another request created the window first; consume from it in the next round.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        throw new AdmissionRejectedException(AdmissionFailure.DependencyUnavailable);
    }

    private async Task<IRequestAdmissionLease> AcquireOnceAsync(
        IReadOnlyList<RateQuota> rates, string[] rateKeys,
        IReadOnlyList<ConcurrentQuota> concurrency, string[] leaseKeys,
        CancellationToken ct)
    {
        using var measurement = NoCtfTelemetry.MeasureAdmission(AdmissionPerformanceStage.AcquireAttempt);
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var now = clock.GetUtcNow();
        var windows = rateKeys.Length == 0 ? new Dictionary<string, RequestAdmissionWindow>() : await ReadWindowsAsync(db, rateKeys, ct);
        var activeLeases = leaseKeys.Length == 0 ? new Dictionary<string, int>() : await ReadLeasesAsync(db, leaseKeys, now, ct);

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

    private static bool IsAdmissionConflict(Exception exception) =>
        TransactionFailureClassifier.IsRetryable(exception)
        || exception is DbUpdateException { InnerException: DbException { SqlState: "23505" } } update
            && update.Entries.All(entry => entry.Entity is RequestAdmissionWindow);

    private static string Key(string prefix, string identity) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            prefix + ':' + identity)));

    private static AdmissionDiagnosticFailure ClassifyFailure(Exception exception) =>
        (exception.GetBaseException() as DbException)?.SqlState switch
        {
            "40001" => AdmissionDiagnosticFailure.Serialization,
            "40P01" => AdmissionDiagnosticFailure.Deadlock,
            _ => AdmissionDiagnosticFailure.Other
        };

    private sealed class UnrestrictedLease(CancellationToken token) : IRequestAdmissionLease
    {
        public CancellationToken Token => token;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

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
                    var now = clock.GetUtcNow();
                    var renewedUntil = now.Add(LeaseDuration);
                    // One atomic statement: only still-live rows of this lease are extended.
                    var renewed = await db.RequestAdmissionLeases
                        .Where(item => item.LeaseId == id && keys.Contains(item.KeyHash) && item.ExpiresAt > now)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAt, renewedUntil)
                            .SetProperty(item => item.ConcurrencyStamp, Guid.NewGuid()), stopping.Token);
                    if (renewed != keys.Length)
                    {
                        await source.CancelAsync();
                        return;
                    }
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
            using var measurement = NoCtfTelemetry.MeasureAdmission(AdmissionPerformanceStage.Release);
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
