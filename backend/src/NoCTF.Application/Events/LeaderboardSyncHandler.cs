using NoCTF.Application.Leaderboard;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Infrastructure;

namespace NoCTF.Application.Events;

/// <summary>
/// Handles post-solve side effects: updates Redis leaderboard cache and fires SignalR notifications.
/// Called by CtfGameMode (and other game modes) after a successful flag submission.
/// </summary>
public class LeaderboardSyncHandler(
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier,
    ICompetitionNotificationOutbox? notificationOutbox = null,
    ApplicationDbContext? db = null,
    ILogger<LeaderboardSyncHandler>? logger = null) : ISubmissionEventHandler
{
    private static readonly LeaderboardRefreshCoordinator SharedRefreshCoordinator = new();
    private readonly LeaderboardRefreshCoordinator _refreshCoordinator = SharedRefreshCoordinator;

    internal LeaderboardSyncHandler(
        ILeaderboardService leaderboardService,
        IRedisLeaderboardCache leaderboardCache,
        IHubNotifierService hubNotifier,
        LeaderboardRefreshCoordinator refreshCoordinator)
        : this(leaderboardService, leaderboardCache, hubNotifier)
    {
        _refreshCoordinator = refreshCoordinator;
    }

    public async Task HandleAsync(SubmissionSolvedEvent solvedEvent, CancellationToken ct = default)
    {
        // Once the solve is committed, request cancellation must not abandon the
        // shared projection refresh. Every caller awaits the same competition task,
        // which also keeps the scoped projection services alive until it completes.
        var refreshTask = _refreshCoordinator.EnqueueAsync(
            solvedEvent.CompetitionId,
            () => RefreshLeaderboardAsync(solvedEvent));

        try
        {
            // Per-solve gameplay events remain compatible and are not coalesced.
            await hubNotifier.NotifyFlagSolvedAsync(
                solvedEvent.CompetitionId,
                solvedEvent.ChallengeId,
                solvedEvent.ChallengeName,
                solvedEvent.TeamId,
                solvedEvent.TeamName,
                solvedEvent.IsFirstBlood,
                CancellationToken.None);
            await QueueBloodNotificationAsync(solvedEvent);
        }
        finally
        {
            // A SignalR failure or a disconnected request must not orphan the accepted refresh.
            await refreshTask;
        }
    }

    private async Task QueueBloodNotificationAsync(SubmissionSolvedEvent solvedEvent)
    {
        if (notificationOutbox is null || db is null || solvedEvent.SolveRank is < 1 or > 3)
            return;
        var type = solvedEvent.SolveRank switch
        {
            1 => CompetitionNotificationTypes.FirstBlood,
            2 => CompetitionNotificationTypes.SecondBlood,
            3 => CompetitionNotificationTypes.ThirdBlood,
            _ => throw new InvalidOperationException()
        };
        try
        {
            var scopeId = solvedEvent.BloodScopeId ?? solvedEvent.ChallengeId;
            notificationOutbox.Add(CompetitionNotification.Create(
                solvedEvent.CompetitionId, type, "challenge", scopeId, solvedEvent.UserId,
                $"blood:{scopeId:N}:rank:{solvedEvent.SolveRank}",
                new
                {
                    problem_title = solvedEvent.ChallengeName,
                    team_name = solvedEvent.TeamName,
                    blood_rank = solvedEvent.SolveRank,
                    occurred_at = (solvedEvent.OccurredAt ?? DateTime.UtcNow).ToString("O")
                }));
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            (logger ?? NullLogger<LeaderboardSyncHandler>.Instance).LogError(exception,
                "Failed to queue QQBot blood notification for submission {SubmissionId}; the solve remains accepted.",
                solvedEvent.SubmissionId);
            db.ChangeTracker.Clear();
        }
    }

    private async Task RefreshLeaderboardAsync(SubmissionSolvedEvent solvedEvent)
    {
        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(
            solvedEvent.CompetitionId,
            CancellationToken.None);
        var entries = await leaderboardService.CalculateLeaderboardAsync(
            solvedEvent.CompetitionId,
            CancellationToken.None);
        await leaderboardCache.UpdateAsync(
            solvedEvent.CompetitionId,
            entries,
            cacheVersion,
            CancellationToken.None);

        var payloads = entries.Select(e => new LeaderboardEntryPayload(
            e.Rank, e.TeamId, e.TeamName, e.TotalScore, e.SolvedCount)).ToArray();
        await hubNotifier.NotifyLeaderboardSnapshotAsync(
            solvedEvent.CompetitionId,
            payloads,
            CancellationToken.None);

        var teamEntry = entries.FirstOrDefault(e => e.TeamId == solvedEvent.TeamId);
        if (teamEntry is not null)
        {
            await hubNotifier.NotifyScoreUpdateAsync(
                solvedEvent.CompetitionId,
                solvedEvent.TeamId,
                solvedEvent.TeamName,
                teamEntry.TotalScore,
                teamEntry.Rank,
                CancellationToken.None);
        }
    }
}

/// <summary>
/// Coalesces full leaderboard projection work by competition. State is capacity bounded,
/// stores only the latest request, and is removed after every successful or failed run.
/// </summary>
internal sealed class LeaderboardRefreshCoordinator
{
    internal const int DefaultCapacity = 128;
    internal static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(50);

    private readonly ConcurrentDictionary<Guid, RefreshState> _states = new();
    private readonly SemaphoreSlim _stateSlots;
    private readonly TimeSpan _debounce;
    private int _maxObservedStateCount;

    public LeaderboardRefreshCoordinator(
        int capacity = DefaultCapacity,
        TimeSpan? debounce = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _stateSlots = new SemaphoreSlim(capacity, capacity);
        _debounce = debounce ?? DefaultDebounce;
        if (_debounce < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(debounce));
    }

    internal int TrackedStateCount => _states.Count;
    internal int MaxObservedStateCount => Volatile.Read(ref _maxObservedStateCount);

    public async Task EnqueueAsync(Guid competitionId, Func<Task> refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);

        Task runner;
        while (true)
        {
            if (_states.TryGetValue(competitionId, out var existing))
            {
                if (existing.TryEnqueue(refresh, out runner))
                    break;

                await Task.Yield();
                continue;
            }

            await _stateSlots.WaitAsync(CancellationToken.None);
            var candidate = new RefreshState(competitionId, RunAsync);
            if (_states.TryAdd(competitionId, candidate))
            {
                UpdateMaxObservedStateCount(_states.Count);
                candidate.TryEnqueue(refresh, out runner);
                break;
            }

            _stateSlots.Release();
        }

        await runner;
    }

    private async Task RunAsync(RefreshState state)
    {
        // Ensure TryEnqueue publishes the runner task before work can complete.
        await Task.Yield();
        try
        {
            await DelayForDebounceAsync();
            while (true)
            {
                var work = state.CaptureLatest();
                try
                {
                    await work.Refresh();
                }
                catch
                {
                    // A request that arrived during the failed refresh owns newer work.
                    // Process it before completing the shared task so its dirty mark is not lost.
                    if (state.TryRetireIfCurrent(work.Version))
                        throw;

                    await DelayForDebounceAsync();
                    continue;
                }

                if (state.TryRetireIfCurrent(work.Version))
                    return;

                await DelayForDebounceAsync();
            }
        }
        finally
        {
            state.Retire();
            if (_states.TryRemove(state.CompetitionId, out _))
                _stateSlots.Release();
        }
    }

    private Task DelayForDebounceAsync()
        => _debounce == TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(_debounce, CancellationToken.None);

    private void UpdateMaxObservedStateCount(int value)
    {
        var current = Volatile.Read(ref _maxObservedStateCount);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref _maxObservedStateCount, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private sealed class RefreshState(
        Guid competitionId,
        Func<RefreshState, Task> runnerFactory)
    {
        private readonly object _gate = new();
        private Func<Task>? _latestRefresh;
        private Task? _runner;
        private long _requestedVersion;
        private bool _retired;

        public Guid CompetitionId { get; } = competitionId;

        public bool TryEnqueue(Func<Task> refresh, out Task runner)
        {
            lock (_gate)
            {
                if (_retired)
                {
                    runner = Task.CompletedTask;
                    return false;
                }

                _latestRefresh = refresh;
                _requestedVersion++;
                _runner ??= runnerFactory(this);
                runner = _runner;
                return true;
            }
        }

        public RefreshWork CaptureLatest()
        {
            lock (_gate)
            {
                return new RefreshWork(
                    _requestedVersion,
                    _latestRefresh ?? throw new InvalidOperationException("Leaderboard refresh work is missing."));
            }
        }

        public bool TryRetireIfCurrent(long completedVersion)
        {
            lock (_gate)
            {
                if (_requestedVersion != completedVersion)
                    return false;

                _retired = true;
                return true;
            }
        }

        public void Retire()
        {
            lock (_gate)
                _retired = true;
        }
    }

    private sealed record RefreshWork(long Version, Func<Task> Refresh);
}
