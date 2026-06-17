namespace NoCTF.Application;

/// <summary>
/// Service interface for pushing real-time notifications to SignalR hub groups.
/// Consumed by the scoring engine and other application services.
/// </summary>
public interface IHubNotifierService
{
    /// <summary>Notify all clients in a competition's leaderboard group of a score change.</summary>
    Task NotifyScoreUpdateAsync(Guid competitionId, Guid teamId, string teamName, long newScore, int newRank, CancellationToken ct = default);

    /// <summary>Push a full leaderboard snapshot to all clients in the competition group.</summary>
    Task NotifyLeaderboardSnapshotAsync(Guid competitionId, IEnumerable<LeaderboardEntryPayload> entries, CancellationToken ct = default);

    /// <summary>Notify all clients in a competition group that a flag was solved.</summary>
    Task NotifyFlagSolvedAsync(Guid competitionId, Guid challengeId, string challengeName, Guid teamId, string teamName, bool isFirstBlood, CancellationToken ct = default);

    /// <summary>Notify all clients in a competition group of a challenge update.</summary>
    Task NotifyChallengeUpdateAsync(Guid competitionId, Guid challengeId, string challengeName, string action, CancellationToken ct = default);

    /// <summary>Notify all clients in a competition group of a competition state change.</summary>
    Task NotifyCompetitionStateChangeAsync(Guid competitionId, string state, CancellationToken ct = default);

    /// <summary>Notify monitor clients of a submission event.</summary>
    Task NotifySubmissionEventAsync(Guid competitionId, Guid submissionId, Guid teamId, string teamName, Guid challengeId, string challengeName, bool isCorrect, CancellationToken ct = default);

    /// <summary>Notify monitor clients of a container lifecycle event.</summary>
    Task NotifyContainerEventAsync(Guid competitionId, Guid containerId, Guid challengeId, Guid teamId, string eventType, CancellationToken ct = default);

    /// <summary>Broadcast a system alert to monitor clients in a competition group.</summary>
    Task NotifySystemAlertAsync(Guid competitionId, string level, string message, CancellationToken ct = default);

    /// <summary>Notify all clients in a competition group that a new AWD round has started.</summary>
    Task NotifyRoundStartedAsync(Guid competitionId, int roundNumber, CancellationToken ct = default);

    /// <summary>Notify all clients in a competition group of a successful AWD attack.</summary>
    Task NotifyAttackLogAsync(
        Guid competitionId,
        Guid attackerTeamId, string attackerTeamName,
        Guid victimTeamId, string victimTeamName,
        Guid challengeId, string challengeName,
        int roundNumber,
        CancellationToken ct = default);

    /// <summary>Notify all clients in a competition group of a KoH control change.</summary>
    Task NotifyKohUpdateAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? controllerTeamId,
        DateTime timestamp,
        CancellationToken ct = default);
}

/// <summary>Payload for leaderboard snapshot entries (avoids dependency on API DTOs).</summary>
public record LeaderboardEntryPayload(int Rank, Guid TeamId, string TeamName, long Score, int SolvedCount);
