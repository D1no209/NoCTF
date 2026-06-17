using Microsoft.AspNetCore.SignalR;
using NoCTF.Application;

namespace NoCTF.API.SignalR;

/// <summary>
/// Concrete implementation of IHubNotifierService using SignalR IHubContext.
/// Registered as a singleton in the API layer.
/// </summary>
public sealed class HubNotifierService : IHubNotifierService
{
    private readonly IHubContext<LeaderboardHub, ILeaderboardClient> _leaderboard;
    private readonly IHubContext<GameHub, IGameNotificationClient> _game;
    private readonly IHubContext<MonitorHub, IMonitorClient> _monitor;

    public HubNotifierService(
        IHubContext<LeaderboardHub, ILeaderboardClient> leaderboard,
        IHubContext<GameHub, IGameNotificationClient> game,
        IHubContext<MonitorHub, IMonitorClient> monitor)
    {
        _leaderboard = leaderboard;
        _game = game;
        _monitor = monitor;
    }

    private static string Group(Guid competitionId) => $"Competition_{competitionId}";

    public Task NotifyScoreUpdateAsync(Guid competitionId, Guid teamId, string teamName, long newScore, int newRank, CancellationToken ct = default)
        => _leaderboard.Clients.Group(Group(competitionId))
            .ReceiveScoreUpdate(new ScoreUpdateDto(teamId, teamName, newScore, newRank, DateTimeOffset.UtcNow));

    public Task NotifyLeaderboardSnapshotAsync(Guid competitionId, IEnumerable<LeaderboardEntryPayload> entries, CancellationToken ct = default)
        => _leaderboard.Clients.Group(Group(competitionId))
            .ReceiveLeaderboardSnapshot(entries.Select(e => new LeaderboardEntryDto(e.Rank, e.TeamId, e.TeamName, e.Score, e.SolvedCount)));

    public Task NotifyFlagSolvedAsync(Guid competitionId, Guid challengeId, string challengeName, Guid teamId, string teamName, bool isFirstBlood, CancellationToken ct = default)
        => _game.Clients.Group(Group(competitionId))
            .ReceiveFlagSolved(new FlagSolvedDto(challengeId, challengeName, teamId, teamName, DateTimeOffset.UtcNow, isFirstBlood));

    public Task NotifyChallengeUpdateAsync(Guid competitionId, Guid challengeId, string challengeName, string action, CancellationToken ct = default)
        => _game.Clients.Group(Group(competitionId))
            .ReceiveChallengeUpdate(new ChallengeUpdateDto(challengeId, challengeName, action, DateTimeOffset.UtcNow));

    public Task NotifyCompetitionStateChangeAsync(Guid competitionId, string state, CancellationToken ct = default)
        => _game.Clients.Group(Group(competitionId))
            .ReceiveCompetitionStateChange(new CompetitionStateDto(competitionId, state, DateTimeOffset.UtcNow));

    public Task NotifySubmissionEventAsync(Guid competitionId, Guid submissionId, Guid teamId, string teamName, Guid challengeId, string challengeName, bool isCorrect, CancellationToken ct = default)
        => _monitor.Clients.Group(Group(competitionId))
            .ReceiveSubmissionEvent(new SubmissionEventDto(submissionId, teamId, teamName, challengeId, challengeName, isCorrect, DateTimeOffset.UtcNow));

    public Task NotifyContainerEventAsync(Guid competitionId, Guid containerId, Guid challengeId, Guid teamId, string eventType, CancellationToken ct = default)
        => _monitor.Clients.Group(Group(competitionId))
            .ReceiveContainerEvent(new ContainerEventDto(containerId, challengeId, teamId, eventType, DateTimeOffset.UtcNow));

    public Task NotifySystemAlertAsync(Guid competitionId, string level, string message, CancellationToken ct = default)
        => _monitor.Clients.Group(Group(competitionId))
            .ReceiveSystemAlert(new SystemAlertDto(level, message, DateTimeOffset.UtcNow));

    public Task NotifyRoundStartedAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
        => _game.Clients.Group(Group(competitionId))
            .ReceiveRoundStarted(new RoundStartedDto(competitionId, roundNumber, DateTimeOffset.UtcNow));

    public Task NotifyAttackLogAsync(
        Guid competitionId,
        Guid attackerTeamId, string attackerTeamName,
        Guid victimTeamId, string victimTeamName,
        Guid challengeId, string challengeName,
        int roundNumber,
        CancellationToken ct = default)
        => _game.Clients.Group(Group(competitionId))
            .ReceiveAttackLog(new AttackLogDto(
                attackerTeamId, attackerTeamName,
                victimTeamId, victimTeamName,
                challengeId, challengeName,
                roundNumber, DateTimeOffset.UtcNow));

    public Task NotifyKohUpdateAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? controllerTeamId,
        DateTime timestamp,
        CancellationToken ct = default)
        => _game.Clients.Group(Group(competitionId))
            .ReceiveKohUpdate(new KohUpdateDto(challengeId, controllerTeamId, new DateTimeOffset(timestamp, TimeSpan.Zero)));
}
