using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Scoring;

namespace NoCTF.Worker;

/// <summary>
/// Materializes the plugin-owned job registry before any processor starts.
/// This turns ambiguous or invalid job registrations into a deterministic
/// host startup failure instead of a data-dependent failure after a task has
/// already been claimed.
/// </summary>
internal sealed class CompetitionJobRegistryStartupValidator(
    IServiceScopeFactory scopeFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        _ = services.GetRequiredService<ICompetitionJobRegistry>().Jobs;
        _ = services.GetRequiredService<IScoreSignalEmitter>();
        _ = services.GetRequiredService<ICtfScoreRebuilder>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
