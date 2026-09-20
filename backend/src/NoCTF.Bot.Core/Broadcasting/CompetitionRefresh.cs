using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Hosting;
using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;
using NoCTF.Bot.Providers;

namespace NoCTF.Bot.Broadcasting;

[Flags]
public enum CompetitionRefreshReason
{
    None = 0,
    Initial = 1,
    Periodic = 2,
    Lifecycle = 4,
    Challenge = 8,
    Scoreboard = 16,
    Announcement = 32,
    Hint = 64,
    TeamModeration = 128
}

public sealed record CompetitionSignal(
    Guid? EventId,
    string Kind,
    DateTimeOffset OccurredAt,
    int RefreshAttempt = 0);

public interface ICompetitionRefreshScheduler
{
    void Schedule(
        Guid competitionId,
        CompetitionRefreshReason reason,
        CompetitionSignal? signal = null,
        TimeSpan? delay = null);
}

public sealed class CompetitionRefreshService(
    NoCtfClient noCtf,
    BotStateStore store,
    BotRuntimeState runtimeState,
    OutboundMessageQueue outbound,
    ChatProviderCatalog providers,
    IOptions<NoCtfBotOptions> noCtfOptions,
    IOptions<RelayOptions> options,
    TimeProvider timeProvider,
    ILogger<CompetitionRefreshService> logger)
    : BackgroundService, ICompetitionRefreshScheduler
{
    private static readonly Guid MaximumGuid =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private readonly Lock pendingLock = new();
    private readonly Dictionary<Guid, PendingRefresh> pending = [];
    private readonly SemaphoreSlim changed = new(0, 1);
    private readonly TimeSpan coalesceDelay = TimeSpan.FromMilliseconds(
        options.Value.RefreshCoalesceMilliseconds);
    private readonly Uri publicBaseUrl = noCtfOptions.Value.PublicBaseUrl;

    public void Schedule(
        Guid competitionId,
        CompetitionRefreshReason reason,
        CompetitionSignal? signal = null,
        TimeSpan? delay = null)
    {
        var dueAt = timeProvider.GetUtcNow() + (delay ?? coalesceDelay);
        lock (pendingLock)
        {
            if (pending.TryGetValue(competitionId, out var existing))
            {
                existing.Reason |= reason;
                if (signal is not null
                    && existing.Signals.All(item => item.EventId != signal.EventId
                        || item.Kind != signal.Kind))
                {
                    existing.Signals.Add(signal);
                }
                if (delay is not null && dueAt > existing.DueAt)
                    existing.DueAt = dueAt;
            }
            else
            {
                pending[competitionId] = new(
                    dueAt,
                    reason,
                    signal is null ? [] : [signal]);
            }
        }
        SignalChanged();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var item = TakeDue();
            if (item is not null)
            {
                await ProcessAsync(item.Value.CompetitionId, item.Value.Refresh, stoppingToken);
                continue;
            }
            var delay = DelayUntilNext();
            if (delay is null)
            {
                await changed.WaitAsync(stoppingToken);
                continue;
            }
            _ = await changed.WaitAsync(delay.Value, stoppingToken);
        }
    }

    private (Guid CompetitionId, PendingRefresh Refresh)? TakeDue()
    {
        lock (pendingLock)
        {
            var now = timeProvider.GetUtcNow();
            var due = pending
                .Where(pair => pair.Value.DueAt <= now)
                .OrderBy(pair => pair.Value.DueAt)
                .FirstOrDefault();
            if (due.Value is null) return null;
            pending.Remove(due.Key);
            return (due.Key, due.Value);
        }
    }

    private TimeSpan? DelayUntilNext()
    {
        lock (pendingLock)
        {
            if (pending.Count == 0) return null;
            var delay = pending.Values.Min(item => item.DueAt) - timeProvider.GetUtcNow();
            return delay <= TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
    }

    private void SignalChanged()
    {
        try
        {
            changed.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
    }

    private async Task ProcessAsync(
        Guid competitionId,
        PendingRefresh refresh,
        CancellationToken ct)
    {
        var subscriptions = store.GetSubscriptions(providers.Active.Id, competitionId)
            .Where(subscription => subscription.SuspendedReason is null)
            .ToArray();
        if (subscriptions.Length == 0 || !runtimeState.IsNoCtfAuthorized) return;

        try
        {
            var competitionResult = await noCtf.GetCompetitionAsync(competitionId, ct);
            if (competitionResult.State == NoCtfReadState.Unavailable)
                ScheduleRetry(competitionId, refresh, TimeSpan.FromSeconds(15));
            if (!HandleReadFailure(competitionId, competitionResult.State, subscriptions)) return;
            var competition = competitionResult.Value!;
            var previousCompetition = store.GetCompetitionSnapshot(competitionId);
            var competitionSnapshot = Snapshot(competition);

            var previousChallenges = store.GetChallengeSnapshots(competitionId);
            IReadOnlyList<ChallengeSnapshot>? challengeSnapshots = null;
            var needsChallenges = refresh.Reason.HasFlag(CompetitionRefreshReason.Initial)
                || refresh.Reason.HasFlag(CompetitionRefreshReason.Periodic)
                || refresh.Reason.HasFlag(CompetitionRefreshReason.Challenge)
                || refresh.Signals.Any(signal => signal.Kind is
                    "FirstBloodAwarded" or "SecondBloodAwarded" or "ThirdBloodAwarded");
            if (needsChallenges)
            {
                var challengeResult = await noCtf.GetChallengesAsync(competitionId, ct);
                if (challengeResult.State == NoCtfReadState.Available)
                {
                    challengeSnapshots = [.. challengeResult.Value!.Items.Select(challenge => Snapshot(competitionId, challenge))];
                }
                else if (challengeResult.State == NoCtfReadState.Unauthorized)
                {
                    MarkUnauthorized();
                    return;
                }
                else if (challengeResult.State == NoCtfReadState.Unavailable)
                {
                    ScheduleRetry(competitionId, refresh, TimeSpan.FromSeconds(15));
                }
            }

            var previousTeams = store.GetTeamSnapshots(competitionId);
            IReadOnlyList<TeamSnapshot>? teamSnapshots = null;
            NoCtfReadResult<ScoreboardSnapshot>? leaderboardResult = null;
            var needsLeaderboard = refresh.Reason.HasFlag(CompetitionRefreshReason.Initial)
                || refresh.Reason.HasFlag(CompetitionRefreshReason.Periodic)
                || refresh.Reason.HasFlag(CompetitionRefreshReason.Scoreboard)
                || refresh.Reason.HasFlag(CompetitionRefreshReason.TeamModeration);
            if (needsLeaderboard)
            {
                leaderboardResult = await noCtf.GetLeaderboardAsync(competitionId, ct);
                if (leaderboardResult.State == NoCtfReadState.Available)
                {
                    teamSnapshots = [.. leaderboardResult.Value!.Teams.Select(team => Snapshot(competitionId, team))];
                }
                else if (leaderboardResult.State == NoCtfReadState.Processing)
                {
                    ScheduleRetry(
                        competitionId,
                        refresh,
                        leaderboardResult.RetryAfter ?? TimeSpan.FromSeconds(2));
                }
                else if (leaderboardResult.State == NoCtfReadState.Unauthorized)
                {
                    MarkUnauthorized();
                    return;
                }
                else if (leaderboardResult.State == NoCtfReadState.NotFound)
                {
                    SuspendCompetition(competitionId, subscriptions, "leaderboard_unavailable");
                    return;
                }
                else if (leaderboardResult.State == NoCtfReadState.Unavailable)
                {
                    ScheduleRetry(
                        competitionId,
                        refresh,
                        leaderboardResult.RetryAfter ?? TimeSpan.FromSeconds(15));
                }
            }

            await ProcessAnnouncementsAsync(
                competitionId,
                subscriptions,
                refresh,
                ct);

            var isBaseline = refresh.Reason.HasFlag(CompetitionRefreshReason.Initial)
                || previousCompetition is null;
            if (!isBaseline)
            {
                var effectiveSignals = refresh.Signals.Where(signal =>
                    (!RequiresChallengeSnapshot(signal.Kind) || challengeSnapshots is not null)
                    && (!RequiresLeaderboardSnapshot(signal.Kind)
                        || leaderboardResult?.State == NoCtfReadState.Available))
                    .ToArray();
                var broadcasts = BroadcastMessageFormatter.Create(
                    competition,
                    previousCompetition!,
                    previousChallenges,
                    challengeSnapshots,
                    previousTeams,
                    teamSnapshots,
                    leaderboardResult?.Value,
                    effectiveSignals);
                foreach (var subscription in subscriptions)
                    foreach (var broadcast in broadcasts.Where(item => Enabled(subscription, item.Category)))
                    {
                        outbound.Enqueue(
                            $"broadcast:{subscription.ProviderId}:{subscription.GroupId}:{competitionId:N}:{broadcast.DedupeKey}",
                            subscription.ProviderId,
                            subscription.GroupId,
                            broadcast.Text);
                    }
                if (leaderboardResult?.Value is { } currentLeaderboard
                    && currentLeaderboard.DataScope != LeaderboardDataScope.Hidden
                    && currentLeaderboard.Visibility != LeaderboardVisibility.Blackout
                    && broadcasts.All(item => item.Category != BroadcastCategory.Scoreboard))
                {
                    foreach (var signal in effectiveSignals.Where(signal =>
                        signal.Kind is "AwdpBreakResolved" or "AwdpFixResolved"
                        && signal.RefreshAttempt < 3))
                    {
                        Schedule(
                            competitionId,
                            CompetitionRefreshReason.Scoreboard,
                            signal with { RefreshAttempt = signal.RefreshAttempt + 1 },
                            TimeSpan.FromSeconds(2));
                    }
                }
            }

            // Queue messages before advancing snapshots. A crash can then only cause a
            // deduplicated replay, never a permanently lost broadcast.
            store.SaveCompetitionSnapshot(competitionSnapshot);
            if (challengeSnapshots is not null)
                store.ReplaceChallengeSnapshots(competitionId, challengeSnapshots);
            if (teamSnapshots is not null)
                store.ReplaceTeamSnapshots(competitionId, teamSnapshots);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Competition {CompetitionId} refresh failed with {ErrorType}.",
                competitionId,
                exception.GetType().Name);
            ScheduleRetry(competitionId, refresh, TimeSpan.FromSeconds(15));
        }
    }

    private bool HandleReadFailure(
        Guid competitionId,
        NoCtfReadState state,
        IReadOnlyCollection<GroupSubscription> subscriptions)
    {
        if (state == NoCtfReadState.Available) return true;
        if (state == NoCtfReadState.Unauthorized) MarkUnauthorized();
        if (state == NoCtfReadState.NotFound)
            SuspendCompetition(competitionId, subscriptions, "competition_unavailable");
        return false;
    }

    private void SuspendCompetition(
        Guid competitionId,
        IReadOnlyCollection<GroupSubscription> subscriptions,
        string reason)
    {
        store.SuspendCompetition(competitionId, reason);
        foreach (var subscription in subscriptions)
        {
            outbound.Enqueue(
                $"subscription-suspended:{subscription.ProviderId}:{subscription.GroupId}:{competitionId:N}:{reason}",
                subscription.ProviderId,
                subscription.GroupId,
                "【NoCTF】比赛已不可访问，该群订阅已暂停。请由群管理员重新订阅后恢复。");
        }
    }

    private void MarkUnauthorized()
    {
        if (!runtimeState.MarkNoCtfUnauthorized()) return;
        logger.LogCritical("NoCTF rejected the BOT access token; API and SignalR reads are stopped.");
        foreach (var subscription in store.GetSubscriptions(providers.Active.Id))
        {
            outbound.Enqueue(
                $"token-invalid:{subscription.ProviderId}:{subscription.GroupId}",
                subscription.ProviderId,
                subscription.GroupId,
                "【NoCTF】BOT 凭据已失效，自动播报和查询已暂停，请联系管理员轮换 Token。");
        }
    }

    private void ScheduleRetry(
        Guid competitionId,
        PendingRefresh refresh,
        TimeSpan delay)
    {
        if (refresh.Signals.Count == 0)
        {
            Schedule(competitionId, refresh.Reason, delay: delay);
            return;
        }
        foreach (var signal in refresh.Signals)
            Schedule(competitionId, refresh.Reason, signal, delay);
    }

    private async Task ProcessAnnouncementsAsync(
        Guid competitionId,
        IReadOnlyCollection<GroupSubscription> subscriptions,
        PendingRefresh refresh,
        CancellationToken ct)
    {
        if (!refresh.Reason.HasFlag(CompetitionRefreshReason.Initial)
            && !refresh.Reason.HasFlag(CompetitionRefreshReason.Periodic)
            && !refresh.Reason.HasFlag(CompetitionRefreshReason.Announcement))
        {
            return;
        }

        var enabled = subscriptions.Where(subscription => subscription.BroadcastEnabled).ToArray();
        if (enabled.Length == 0) return;
        var checkpoints = enabled.ToDictionary(
            subscription => (subscription.ProviderId, subscription.GroupId),
            subscription => store.GetAnnouncementCheckpoint(
                subscription.ProviderId,
                subscription.GroupId,
                competitionId));
        var oldest = checkpoints.Values
            .Where(checkpoint => checkpoint is not null)
            .MinBy(checkpoint => (checkpoint!.PublishedAt, checkpoint.AnnouncementId));
        var announcements = new List<CompetitionAnnouncement>();
        string? cursor = null;
        do
        {
            var result = await noCtf.GetAnnouncementsAsync(competitionId, cursor, ct);
            if (result.State == NoCtfReadState.Unauthorized)
            {
                MarkUnauthorized();
                return;
            }
            if (result.State == NoCtfReadState.NotFound)
            {
                logger.LogDebug(
                    "Public announcement feed is not available for competition {CompetitionId}; announcement forwarding is skipped.",
                    competitionId);
                return;
            }
            if (result.State != NoCtfReadState.Available || result.Value is null)
            {
                ScheduleRetry(competitionId, refresh, TimeSpan.FromSeconds(15));
                return;
            }
            announcements.AddRange(result.Value.Items);
            cursor = result.Value.NextCursor;
            if (oldest is null
                || result.Value.Items.Count == 0
                || IsAtOrBefore(result.Value.Items[^1], oldest))
            {
                break;
            }
        }
        while (cursor is not null);

        var newest = announcements
            .OrderByDescending(item => item.PublishedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();
        foreach (var subscription in enabled)
        {
            var checkpoint = checkpoints[(subscription.ProviderId, subscription.GroupId)];
            if (checkpoint is null)
            {
                store.SaveAnnouncementCheckpoint(new(
                    subscription.ProviderId,
                    subscription.GroupId,
                    competitionId,
                    newest?.PublishedAt ?? timeProvider.GetUtcNow(),
                    newest?.Id ?? MaximumGuid));
                continue;
            }
            foreach (var announcement in announcements
                .Where(item => IsAfter(item, checkpoint))
                .OrderBy(item => item.PublishedAt)
                .ThenBy(item => item.Id))
            {
                outbound.Enqueue(
                    $"announcement:{subscription.ProviderId}:{subscription.GroupId}:{announcement.Id:N}",
                    subscription.ProviderId,
                    subscription.GroupId,
                    $"【NoCTF】公告｜{announcement.Title}\n{announcement.Body}\n{new Uri(publicBaseUrl, $"competitions/{competitionId:D}")}");
            }
            if (newest is not null && IsAfter(newest, checkpoint))
            {
                store.SaveAnnouncementCheckpoint(new(
                    subscription.ProviderId,
                    subscription.GroupId,
                    competitionId,
                    newest.PublishedAt,
                    newest.Id));
            }
        }
    }

    private static bool IsAfter(
        CompetitionAnnouncement announcement,
        AnnouncementCheckpoint checkpoint) =>
        announcement.PublishedAt > checkpoint.PublishedAt
        || announcement.PublishedAt == checkpoint.PublishedAt
        && announcement.Id.CompareTo(checkpoint.AnnouncementId) > 0;

    private static bool IsAtOrBefore(
        CompetitionAnnouncement announcement,
        AnnouncementCheckpoint checkpoint) => !IsAfter(announcement, checkpoint);

    private static bool RequiresChallengeSnapshot(string kind) => kind is
        "ChallengePublished" or "ChallengeDescriptionUpdated"
        or "FirstBloodAwarded" or "SecondBloodAwarded" or "ThirdBloodAwarded";

    private static bool RequiresLeaderboardSnapshot(string kind) => kind is
        "FirstBloodAwarded" or "SecondBloodAwarded" or "ThirdBloodAwarded"
        or "ScoringRecorded" or "AwdpBreakResolved" or "AwdpFixResolved";

    private static bool Enabled(GroupSubscription subscription, BroadcastCategory category) =>
        category switch
        {
            BroadcastCategory.General => subscription.BroadcastEnabled,
            BroadcastCategory.Scoreboard => subscription.ScoreboardEnabled,
            BroadcastCategory.Blood => subscription.BloodEnabled,
            _ => false
        };

    private CompetitionSnapshot Snapshot(Competition competition) =>
        new(
            competition.Id,
            competition.Status,
            competition.StartTime,
            competition.EndTime,
            Hash($"{competition.Title}|{competition.Status}|{competition.StartTime:O}|{competition.EndTime:O}"),
            timeProvider.GetUtcNow());

    private static ChallengeSnapshot Snapshot(Guid competitionId, Challenge challenge) =>
        new(
            competitionId,
            challenge.Id,
            challenge.DisplayTitle,
            challenge.IsPublished,
            Hash($"{challenge.DisplayTitle}|{challenge.Description}|{challenge.Direction}|{challenge.UpdatedAt:O}"));

    private static TeamSnapshot Snapshot(Guid competitionId, ScoreboardTeam team)
    {
        var achievements = (team.Achievements ?? [])
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.CompetitionChallengeId)
            .ToArray();
        var json = JsonSerializer.Serialize(achievements);
        return new(
            competitionId,
            team.TeamId,
            team.TeamName,
            team.Rank,
            team.TotalScore,
            Hash(json),
            json);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class PendingRefresh(
        DateTimeOffset dueAt,
        CompetitionRefreshReason reason,
        List<CompetitionSignal> signals)
    {
        public DateTimeOffset DueAt { get; set; } = dueAt;
        public CompetitionRefreshReason Reason { get; set; } = reason;
        public List<CompetitionSignal> Signals { get; } = signals;
    }
}

public enum BroadcastCategory { General, Scoreboard, Blood }

public sealed record GeneratedBroadcast(
    string DedupeKey,
    BroadcastCategory Category,
    string Text);
