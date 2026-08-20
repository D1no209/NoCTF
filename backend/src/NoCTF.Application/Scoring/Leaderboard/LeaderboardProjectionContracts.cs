using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record LeaderboardProjectionInput(
    Guid CompetitionId,
    GameMode Mode,
    IReadOnlyList<LeaderboardTeamFact> Teams,
    IReadOnlyList<LeaderboardGameplayFact> GameplayFacts,
    IReadOnlyList<LeaderboardChallengeFact>? Challenges = null,
    string? CompetitionConfigurationJson = null,
    DateTimeOffset? CompetitionStartTime = null,
    IReadOnlyList<CompetitionLifecycleTransition>? LifecycleAudits = null,
    IReadOnlyList<LeaderboardAwdRoundFact>? AwdRounds = null,
    DateTimeOffset? ProjectedAt = null,
    CompetitionStatus? CompetitionStatus = null,
    IReadOnlyList<LeaderboardGameplayFact>? ScoreboardGameplayFacts = null,
    int? ScoreboardRoundWindowEnd = null);

public sealed record LeaderboardTeamFact(
    Guid Id,
    string Name,
    bool IsBanned,
    bool IsDeleted,
    DateTimeOffset RegisteredAt = default,
    string TrackKey = CompetitionTrackConfiguration.DefaultTrackKey,
    bool EarnsScore = true,
    bool EarnsBlood = true,
    bool AffectsDynamicChallengeScore = true,
    bool VisibleOnLeaderboard = true,
    bool AffectsCompetitiveResults = true);
public sealed record LeaderboardChallengeFact(
    Guid Id,
    string Direction,
    string Title,
    bool IsDeleted,
    string? ConfigurationJson = null,
    int Order = 0,
    bool IsPublished = true,
    int Revision = 0);

public sealed record LeaderboardGameplayFact(
    Guid GameplayFactId,
    Guid? TeamId,
    Guid? CompetitionChallengeId,
    GameplayFactKind Kind,
    DateTimeOffset OccurredAt,
    GameplayFactState State,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    GameplayFactReferenceKind? ReferenceKind = null,
    Guid? ReferenceId = null,
    Guid? VictimTeamId = null,
    string? SubmitterName = null,
    string? Value = null,
    long? HintCost = null,
    Guid? ActorUserId = null,
    int Multiplicity = 1,
    DateTimeOffset? LastOccurredAt = null);

public sealed record LeaderboardAwdRoundFact(
    Guid CompetitionChallengeId,
    Guid TeamId,
    Guid RoundId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record GameModeLeaderboardProjection(
    IReadOnlyList<LeaderboardEntry> Entries,
    IReadOnlyList<LeaderboardCellFact> Cells,
    IReadOnlyDictionary<Guid, long>? CurrentScores = null,
    IReadOnlyDictionary<Guid, long>? CurrentBreakScores = null,
    IReadOnlyDictionary<Guid, long>? CurrentFixScores = null,
    int? CurrentRound = null,
    int? SettledThroughRound = null,
    int? RoundDurationSeconds = null,
    int? CurrentRoundRemainingSeconds = null);

public interface IGameModeLeaderboardProjector
{
    GameMode Mode { get; }
    GameModeLeaderboardProjection Project(LeaderboardProjectionInput input);
}

public interface ILeaderboardProjectorCatalog
{
    IGameModeLeaderboardProjector Get(GameMode mode);
}

public sealed record LeaderboardProjectionResult(
    IReadOnlyList<LeaderboardEntry> Entries,
    IReadOnlyList<LeaderboardChallengeInfo> Challenges,
    int? CurrentRound = null,
    int? SettledThroughRound = null,
    int? RoundDurationSeconds = null,
    int? CurrentRoundRemainingSeconds = null);

public interface ILeaderboardProjectionEngine
{
    LeaderboardProjectionResult Project(LeaderboardProjectionInput input);

    ScoreboardProjection ProjectScoreboard(LeaderboardProjectionInput input);
}
