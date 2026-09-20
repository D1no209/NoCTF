using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Broadcasting;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Hosting;
using NoCTF.Bot.Persistence;
using NoCTF.Bot.Providers;

namespace NoCTF.Bot.NoCtf;

public interface ICompetitionSubscriptionMonitor
{
    void SignalChanged();
}

public sealed class CompetitionSignalRService(
    IOptions<NoCtfBotOptions> options,
    BotStateStore store,
    BotRuntimeState runtimeState,
    ChatProviderCatalog providers,
    ICompetitionRefreshScheduler refresh,
    ILogger<CompetitionSignalRService> logger)
    : BackgroundService, ICompetitionSubscriptionMonitor
{
    private readonly NoCtfBotOptions options = options.Value;
    private readonly SemaphoreSlim changed = new(0, 1);

    public void SignalChanged() => Wake();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (changed.Wait(0, stoppingToken)) { }
            if (!runtimeState.IsNoCtfAuthorized)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }
            var competitionIds = store.GetSubscriptions(providers.Active.Id)
                .Where(subscription => subscription.SuspendedReason is null)
                .Select(subscription => subscription.CompetitionId)
                .Distinct()
                .ToArray();
            if (competitionIds.Length == 0)
            {
                await changed.WaitAsync(stoppingToken);
                continue;
            }

            await using var connection = BuildConnection();
            connection.Closed += exception =>
            {
                if (exception is not null)
                {
                    logger.LogWarning(
                        "NoCTF SignalR connection closed with {ErrorType}.",
                        exception.GetType().Name);
                }
                Wake();
                return Task.CompletedTask;
            };
            connection.Reconnected += async _ =>
            {
                await JoinAllAsync(connection, competitionIds, stoppingToken);
                foreach (var competitionId in competitionIds)
                    refresh.Schedule(competitionId, CompetitionRefreshReason.Periodic);
            };

            try
            {
                await connection.StartAsync(stoppingToken);
                await JoinAllAsync(connection, competitionIds, stoppingToken);
                logger.LogInformation(
                    "Connected to NoCTF SignalR for {CompetitionCount} competition(s).",
                    competitionIds.Length);
                await changed.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "NoCTF SignalR is unavailable ({ErrorType}); retrying in 15 seconds.",
                    exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            finally
            {
                if (connection.State != HubConnectionState.Disconnected)
                    await connection.StopAsync(CancellationToken.None);
            }
        }
    }

    private HubConnection BuildConnection()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(options.BaseUrl, "hubs/v1/competitions"), connectionOptions =>
            {
                connectionOptions.AccessTokenProvider = () => Task.FromResult<string?>(options.AccessToken);
            })
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            ])
            .Build();
        connection.On<ScoreboardUpdated>("scoreboardUpdated", notification =>
        {
            refresh.Schedule(
                notification.CompetitionId,
                CompetitionRefreshReason.Scoreboard,
                new(null, "ScoreboardUpdated", DateTimeOffset.UtcNow));
        });
        connection.On<CompetitionLifecycleChanged>("competitionLifecycleChanged", notification =>
        {
            refresh.Schedule(
                notification.CompetitionId,
                CompetitionRefreshReason.Lifecycle,
                new(null, "CompetitionLifecycleChanged", notification.OccurredAt));
        });
        connection.On<CompetitionEventChanged>("competitionEventChanged", notification =>
        {
            if (TryClassify(notification.Kind, out var reason))
            {
                refresh.Schedule(
                    notification.CompetitionId,
                    reason,
                    new(notification.EventId, notification.Kind, notification.OccurredAt));
            }
        });
        connection.On<object>("gameplayFactStateChanged", static _ => { });
        return connection;
    }

    private void Wake()
    {
        try
        {
            changed.Release();
        }
        catch (SemaphoreFullException)
        {
            // A reconnect/reconfiguration wake-up is already pending.
        }
    }

    private static async Task JoinAllAsync(
        HubConnection connection,
        IEnumerable<Guid> competitionIds,
        CancellationToken ct)
    {
        foreach (var competitionId in competitionIds)
            await connection.InvokeAsync("JoinCompetition", competitionId, ct);
    }

    private static bool TryClassify(string kind, out CompetitionRefreshReason reason)
    {
        reason = kind switch
        {
            "ChallengePublished" or "ChallengeDescriptionUpdated" =>
                CompetitionRefreshReason.Challenge,
            "HintPublished" => CompetitionRefreshReason.Hint,
            "FirstBloodAwarded" or "SecondBloodAwarded" or "ThirdBloodAwarded" or "ScoringRecorded" =>
                CompetitionRefreshReason.Scoreboard,
            "AnnouncementPublished" => CompetitionRefreshReason.Announcement,
            "TeamBanned" or "TeamBanCorrectionPublished" => CompetitionRefreshReason.TeamModeration,
            "AwdpBreakResolved" or "AwdpFixResolved" => CompetitionRefreshReason.Scoreboard,
            _ => CompetitionRefreshReason.None
        };
        return reason != CompetitionRefreshReason.None;
    }
}
