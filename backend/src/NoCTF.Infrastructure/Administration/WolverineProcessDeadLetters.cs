using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Postgresql;
using Wolverine.Runtime;

namespace NoCTF.Infrastructure.Administration;

public interface IProcessDeadLetterStore
{
    Task<IReadOnlyList<DeadLetterEnvelope>> ListAsync(
        int limit,
        CancellationToken cancellationToken);
    Task<DeadLetterEnvelope?> FindAsync(Guid messageId, CancellationToken cancellationToken);
    Task<bool> ReplayAsync(Guid messageId, CancellationToken cancellationToken);
}

public static class WolverinePersistenceSchemas
{
    public const string Api = "wolverine_api";
    public const string Worker = "wolverine_worker";
    public const string Runner = "wolverine_runner";

    public static IReadOnlyList<string> All { get; } = [Api, Worker, Runner];
}

public sealed class WolverineProcessDeadLetters : IProcessDeadLetterStore, IAsyncDisposable
{
    private readonly IHost storageHost;
    private readonly IWolverineRuntime storageRuntime;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    public WolverineProcessDeadLetters(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(options =>
        {
            options.ServiceName = "NoCTF.Admin.DeadLetters";
            options.PersistMessagesWithPostgresql(
                connectionString,
                WolverinePersistenceSchemas.Api);
            options.PersistMessagesWithPostgresql(
                connectionString,
                WolverinePersistenceSchemas.Worker,
                MessageStoreRole.Ancillary);
            options.PersistMessagesWithPostgresql(
                connectionString,
                WolverinePersistenceSchemas.Runner,
                MessageStoreRole.Ancillary);
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        });
        storageHost = builder.Build();
        storageRuntime = storageHost.Services.GetRequiredService<IWolverineRuntime>();
    }

    public async Task<IReadOnlyList<DeadLetterEnvelope>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var stores = await storageRuntime.Stores.FindAllAsync();
        var queryTasks = stores.Select(store => store.DeadLetters.QueryAsync(new DeadLetterEnvelopeQuery
        {
            PageNumber = 1,
            PageSize = limit
        }, cancellationToken));
        var results = await Task.WhenAll(queryTasks);
        return results
            .SelectMany(result => result.Envelopes)
            .OrderBy(envelope => envelope.SentAt)
            .Take(limit)
            .ToArray();
    }

    public async Task<DeadLetterEnvelope?> FindAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var stores = await storageRuntime.Stores.FindAllAsync();
        var query = new DeadLetterEnvelopeQuery([messageId])
        {
            PageNumber = 1,
            PageSize = 1
        };
        foreach (var store in stores)
        {
            var result = await store.DeadLetters.QueryAsync(query, cancellationToken);
            if (result.Envelopes.Count != 0)
                return result.Envelopes[0];
        }
        return null;
    }

    public async Task<bool> ReplayAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var stores = await storageRuntime.Stores.FindAllAsync();
        var query = new DeadLetterEnvelopeQuery([messageId])
        {
            PageNumber = 1,
            PageSize = 1
        };
        foreach (var store in stores)
        {
            var result = await store.DeadLetters.QueryAsync(query, cancellationToken);
            if (result.Envelopes.Count == 0)
                continue;
            await store.DeadLetters.ReplayAsync(query, cancellationToken);
            return true;
        }
        return false;
    }

    public ValueTask DisposeAsync()
    {
        storageHost.Dispose();
        initializationLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;
        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
                return;
            var stores = await storageRuntime.Stores.FindAllAsync();
            foreach (var store in stores)
                await store.Admin.MigrateAsync();
            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

}
