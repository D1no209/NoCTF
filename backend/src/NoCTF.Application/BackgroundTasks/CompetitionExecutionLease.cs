using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.Application.BackgroundTasks;

public interface ICompetitionExecutionLease
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        ApplicationDbContext db,
        string engineKey,
        Guid competitionId,
        CancellationToken ct = default);
}

public sealed class CompetitionExecutionLease : ICompetitionExecutionLease
{
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> LocalLocks = new();

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        ApplicationDbContext db,
        string engineKey,
        Guid competitionId,
        CancellationToken ct = default)
    {
        var lockKey = CreateLockKey(engineKey, competitionId);
        if (!db.Database.IsRelational() ||
            db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true)
        {
            var semaphore = LocalLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(0, ct)
                ? new LocalLease(semaphore)
                : null;
        }

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

    private sealed class LocalLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PostgresLease(
        DbConnection connection,
        long key,
        bool closeWhenReleased) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (connection.State != ConnectionState.Open)
                return;

            try
            {
                await ExecuteBooleanAsync(connection, "SELECT pg_advisory_unlock(@key)", key, CancellationToken.None);
            }
            finally
            {
                if (closeWhenReleased && connection.State == ConnectionState.Open)
                    await connection.CloseAsync();
            }
        }
    }
}
