namespace NoCTF.API.SignalR;

/// <summary>
/// Strongly-typed client interface for game event notifications.
/// </summary>
public interface IGameNotificationClient
{
    /// <summary>Sent when a flag is successfully submitted.</summary>
    Task ReceiveFlagSolved(FlagSolvedDto notification);

    /// <summary>Sent when a challenge is released or updated.</summary>
    Task ReceiveChallengeUpdate(ChallengeUpdateDto notification);

    /// <summary>Sent when competition state changes (started, paused, ended).</summary>
    Task ReceiveCompetitionStateChange(CompetitionStateDto state);

    /// <summary>Sent when a new AWD round starts.</summary>
    Task ReceiveRoundStarted(RoundStartedDto notification);

    /// <summary>Sent when an AWD attack (flag capture) succeeds.</summary>
    Task ReceiveAttackLog(AttackLogDto log);

    /// <summary>Sent when a KoH hill changes controller.</summary>
    Task ReceiveKohUpdate(KohUpdateDto update);
}

public record FlagSolvedDto(
    Guid ChallengeId,
    string ChallengeName,
    Guid TeamId,
    string TeamName,
    DateTimeOffset SolvedAt,
    bool IsFirstBlood);

public record ChallengeUpdateDto(
    Guid ChallengeId,
    string ChallengeName,
    string Action,   // "released" | "updated" | "closed"
    DateTimeOffset Timestamp);

public record CompetitionStateDto(
    Guid CompetitionId,
    string State,    // "started" | "paused" | "ended"
    DateTimeOffset Timestamp);

public record RoundStartedDto(
    Guid CompetitionId,
    int RoundNumber,
    DateTimeOffset StartedAt);

public record AttackLogDto(
    Guid AttackerTeamId,
    string AttackerTeamName,
    Guid VictimTeamId,
    string VictimTeamName,
    Guid ChallengeId,
    string ChallengeName,
    int RoundNumber,
    DateTimeOffset Timestamp);

public record KohUpdateDto(
    Guid ChallengeId,
    Guid? ControllerTeamId,
    DateTimeOffset Timestamp);
