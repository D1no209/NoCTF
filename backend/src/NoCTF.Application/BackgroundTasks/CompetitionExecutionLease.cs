using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using StackExchange.Redis;

namespace NoCTF.Application.BackgroundTasks;

public interface IExecutionLease : IAsyncDisposable
{
    /// <summary>
    /// Cancels when the backing lock can no longer be proven to be owned by
    /// this process. Callers must link this token to all work performed while
    /// the lease is held so a stale owner cannot commit after the TTL expires.
    /// </summary>
    CancellationToken LostToken { get; }
}

public interface ICompetitionExecutionLease
{
    Task<IExecutionLease?> TryAcquireAsync(
        ApplicationDbContext db,
        string engineKey,
        Guid competitionId,
        CancellationToken ct = default);
}

public static class CompetitionExecutionLeaseKeys
{
    /// <summary>
    /// Short critical section used while a runtime candidate is made durable.
    /// Destructive lifecycle operations take the same lease while committing
    /// their tombstone, so every external create is either rejected or visible
    /// to the subsequent cleanup scan.
    /// </summary>
    public const string RuntimePreparation = "runtime-preparation";

    public static string ChallengeInstance(Guid teamId, Guid challengeId)
        => $"challenge-instance:{teamId:N}:{challengeId:N}";
}

public sealed class CompetitionExecutionLease(IConnectionMultiplexer? redis = null) : ICompetitionExecutionLease
{
    private static readonly ConcurrentDictionary<long, LocalLockState> LocalLocks = new();
    private static readonly TimeSpan RedisLeaseDuration = TimeSpan.FromMinutes(5);

    public async Task<IExecutionLease?> TryAcquireAsync(
        ApplicationDbContext db,
        string engineKey,
        Guid competitionId,
        CancellationToken ct = default)
    {
        var lockKey = CreateLockKey(engineKey, competitionId);
        // Database-backed transitions use a session advisory lock. Unlike a
        // TTL lease, it cannot expire while a paused owner still has in-flight
        // database or Runner work, which removes the stale-owner fencing gap.
        if (db.Database.IsRelational() &&
            db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var connection = db.Database.GetDbConnection();
            var closeWhenReleased = connection.State != ConnectionState.Open;
            if (closeWhenReleased)
                await connection.OpenAsync(ct);

            try
            {
                if (!await ExecuteBooleanAsync(connection, "SELECT pg_try_advisory_lock(@key)", lockKey, ct))
                {
                    if (closeWhenReleased)
                        await connection.CloseAsync();
                    return null;
                }

                return new PostgresLease(connection, lockKey, closeWhenReleased);
            }
            catch
            {
                if (closeWhenReleased && connection.State == ConnectionState.Open)
                    await connection.CloseAsync();
                throw;
            }
        }

        // Non-relational hosts (primarily test/development providers) can use
        // Redis for cross-process coordination when one is explicitly wired.
        if (redis is not null)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var database = redis.GetDatabase();
                var redisKey = (RedisKey)$"noctf:engine-lease:{lockKey}";
                var owner = Guid.NewGuid().ToString("N");
                if (!await database.StringSetAsync(redisKey, owner, RedisLeaseDuration, When.NotExists))
                    return null;
                return new RedisLease(database, redisKey, owner, RedisLeaseDuration);
            }
            catch (RedisException)
            {
                // Do not switch lock authorities while another replica may still
                // own a Redis lease. Skipping this run is safer than split-brain
                // execution; PostgreSQL/local locking is only used when Redis was
                // not configured for this host.
                return null;
            }
        }

        while (true)
        {
            var state = LocalLocks.GetOrAdd(lockKey, static _ => new LocalLockState());
            if (!state.TryAddReference())
            {
                LocalLocks.TryRemove(new KeyValuePair<long, LocalLockState>(lockKey, state));
                continue;
            }

            try
            {
                if (await state.Semaphore.WaitAsync(0, ct))
                    return new LocalLease(lockKey, state);
            }
            catch
            {
                ReleaseLocalReference(lockKey, state);
                throw;
            }

            ReleaseLocalReference(lockKey, state);
            return null;
        }
    }

    private static long CreateLockKey(string engineKey, Guid competitionId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"noctf:{engineKey}:{competitionId:N}"));
        return BitConverter.ToInt64(bytes, 0);
    }

    private static async Task<bool> ExecuteBooleanAsync(
        DbConnection connection,
        string sql,
        long key,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ReleaseLocalReference(long key, LocalLockState state)
    {
        if (state.ReleaseReference())
            LocalLocks.TryRemove(new KeyValuePair<long, LocalLockState>(key, state));
    }

    private sealed class LocalLockState
    {
        private readonly object _sync = new();
        private int _references;
        private bool _retired;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddReference()
        {
            lock (_sync)
            {
                if (_retired)
                    return false;
                _references++;
                return true;
            }
        }

        public bool ReleaseReference()
        {
            lock (_sync)
            {
                _references--;
                if (_references != 0)
                    return false;
                _retired = true;
                return true;
            }
        }
    }

    private sealed class LocalLease(long key, LocalLockState state) : IExecutionLease
    {
        private int _disposed;
        public CancellationToken LostToken => CancellationToken.None;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;

            state.Semaphore.Release();
            ReleaseLocalReference(key, state);
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class PostgresLease : IExecutionLease
    {
        private readonly DbConnection _connection;
        private readonly long _key;
        private readonly bool _closeWhenReleased;
        private readonly CancellationTokenSource _lostCts = new();
        private int _disposed;

        public PostgresLease(
            DbConnection connection,
            long key,
            bool closeWhenReleased)
        {
            _connection = connection;
            _key = key;
            _closeWhenReleased = closeWhenReleased;
            _connection.StateChange += OnConnectionStateChanged;

            // The advisory lock is scoped to one PostgreSQL session. If the
            // connection is already gone, ownership cannot be proven even if a
            // pool later gives this DbContext another physical connection.
            if ((_connection.State & ConnectionState.Open) == 0)
                SignalLost();
        }

        public CancellationToken LostToken => _lostCts.Token;

        private void OnConnectionStateChanged(object? sender, StateChangeEventArgs args)
        {
            if (Volatile.Read(ref _disposed) == 0 &&
                (args.CurrentState & ConnectionState.Open) == 0)
            {
                SignalLost();
            }
        }

        private void SignalLost()
        {
            try
            {
                _lostCts.Cancel(throwOnFirstException: false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (AggregateException)
            {
                // A consumer cancellation callback must not escape through the
                // provider's StateChange event and destabilize the connection.
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _connection.StateChange -= OnConnectionStateChanged;
            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    await ExecuteBooleanAsync(
                        _connection,
                        "SELECT pg_advisory_unlock(@key)",
                        _key,
                        CancellationToken.None);
                }
            }
            finally
            {
                try
                {
                    if (_closeWhenReleased && _connection.State == ConnectionState.Open)
                        await _connection.CloseAsync();
                }
                finally
                {
                    _lostCts.Dispose();
                }
            }
        }
    }

    private sealed class RedisLease : IExecutionLease
    {
        private const string RenewScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
        private const string ReleaseScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly RedisValue _owner;
        private readonly TimeSpan _duration;
        private readonly CancellationTokenSource _renewalCts = new();
        private readonly CancellationTokenSource _lostCts = new();
        private readonly Task _renewalTask;
        private long _confirmedUntilTimestamp;

        public RedisLease(IDatabase database, RedisKey key, RedisValue owner, TimeSpan duration)
        {
            _database = database;
            _key = key;
            _owner = owner;
            _duration = duration;
            _confirmedUntilTimestamp = AddDuration(Stopwatch.GetTimestamp(), duration);
            _renewalTask = RenewAsync(_renewalCts.Token);
        }

        public CancellationToken LostToken => _lostCts.Token;

        private async Task RenewAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromTicks(_duration.Ticks / 3));
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    var renewalStartedAt = Stopwatch.GetTimestamp();
                    var renewed = (long)await _database.ScriptEvaluateAsync(
                            RenewScript,
                            [_key],
                            [_owner, (long)_duration.TotalMilliseconds])
                        .WaitAsync(ct);
                    if (renewed == 0)
                    {
                        await _lostCts.CancelAsync();
                        return;
                    }

                    // Redis applies the TTL while handling the request. Basing
                    // the local proof window on request start deliberately
                    // underestimates ownership when the network is slow.
                    Volatile.Write(
                        ref _confirmedUntilTimestamp,
                        AddDuration(renewalStartedAt, _duration));
                }
                catch (RedisException)
                {
                    if (Stopwatch.GetTimestamp() >= Volatile.Read(ref _confirmedUntilTimestamp))
                    {
                        await _lostCts.CancelAsync();
                        return;
                    }
                }
            }
        }

        private static long AddDuration(long timestamp, TimeSpan duration)
            => checked(timestamp + (long)(duration.TotalSeconds * Stopwatch.Frequency));

        public async ValueTask DisposeAsync()
        {
            await _renewalCts.CancelAsync();
            try
            {
                await _renewalTask;
            }
            catch (OperationCanceledException) when (_renewalCts.IsCancellationRequested)
            {
            }
            finally
            {
                _renewalCts.Dispose();
            }

            try
            {
                await _database.ScriptEvaluateAsync(ReleaseScript, [_key], [_owner]);
            }
            catch (RedisException)
            {
                // The key will expire if Redis is temporarily unavailable.
            }
            finally
            {
                _lostCts.Dispose();
            }
        }
    }
}
