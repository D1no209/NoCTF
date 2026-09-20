using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Hosting;
using NoCTF.Bot.Persistence;
using NoCTF.Bot.Providers;

namespace NoCTF.Bot.Broadcasting;

public sealed class PeriodicSnapshotService(
    BotStateStore store,
    BotRuntimeState runtimeState,
    ICompetitionRefreshScheduler refresh,
    ChatProviderCatalog providers,
    IOptions<RelayOptions> options) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(options.Value.SnapshotPollSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!runtimeState.IsNoCtfAuthorized) return;
            foreach (var competitionId in store.GetSubscriptions(providers.Active.Id)
                .Where(subscription => subscription.SuspendedReason is null)
                .Select(subscription => subscription.CompetitionId)
                .Distinct())
            {
                refresh.Schedule(competitionId, CompetitionRefreshReason.Periodic);
            }
        }
    }
}
