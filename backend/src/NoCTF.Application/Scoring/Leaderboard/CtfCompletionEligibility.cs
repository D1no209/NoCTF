using System.Linq.Expressions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;

namespace NoCTF.Application.Scoring.Leaderboard;

public static class CtfCompletionEligibility
{
    // Query preselection and in-memory evaluation share this capability contract.
    public static Expression<Func<Team, bool>> ParticipatingTeams => team =>
        team.RegistrationStatus == TeamRegistrationStatus.Approved && !team.IsBanned && team.DeletedAt == null;

    public static bool CanParticipate(TeamRegistrationStatus status, bool banned, bool deleted) =>
        status == TeamRegistrationStatus.Approved && !banned && !deleted;

    // Projection inputs have already been restricted to approved teams by their reader.
    public static bool IsActive(LeaderboardTeamFact team) =>
        CanParticipate(TeamRegistrationStatus.Approved, team.IsBanned, team.IsDeleted);
    public static bool EarnsScore(LeaderboardTeamFact team) => IsActive(team) && team.EarnsScore;
    public static bool EarnsBlood(LeaderboardTeamFact team) => IsActive(team) && team.EarnsBlood;
    public static bool AffectsDynamicScore(LeaderboardTeamFact team) => IsActive(team) && team.AffectsDynamicChallengeScore;

    public static CompetitionTrackDefinition Track(CompetitionTrackConfiguration configuration, string key) =>
        configuration.Find(key) ?? configuration.DefaultTrack;

    public static GameplayFactKind CompletionKind(CtfInteractionKind interaction) => interaction switch
    {
        CtfInteractionKind.FlagSubmission => GameplayFactKind.FlagAttempt,
        CtfInteractionKind.PatchVerification => GameplayFactKind.FixAttempt,
        _ => throw new InvalidOperationException("Unknown CTF interaction kind.")
    };

    public static bool Matches(GameplayFactKind kind, CtfInteractionKind interaction) => kind == CompletionKind(interaction);

    public static Expression<Func<GameplayFact, bool>> Before(DateTimeOffset at, Guid id) =>
        fact => fact.OccurredAt < at || fact.OccurredAt == at && fact.Id.CompareTo(id) < 0;

    public static bool IsBefore(DateTimeOffset leftAt, Guid leftId, DateTimeOffset rightAt, Guid rightId) =>
        leftAt < rightAt || leftAt == rightAt && leftId.CompareTo(rightId) < 0;
}
