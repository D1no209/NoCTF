using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.GameplayFacts.Intake;

internal static class GameplayFactAdmissionPersistence
{
    public static async Task<GameplayFactAdmissionSnapshot?> LoadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var scope = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Join(
                db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge => challenge.Id == competitionChallengeId),
                competition => competition.Id,
                challenge => challenge.CompetitionId,
                (competition, challenge) => new { Competition = competition, CompetitionChallenge = challenge })
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.CompetitionChallenge.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new { item.Competition, item.CompetitionChallenge, Challenge = challenge })
            .Join(
                db.Teams.AsNoTracking().Where(team => team.MemberIds.Contains(userId)),
                item => item.Competition.Id,
                team => team.CompetitionId,
                (item, team) => new { item.Competition, item.CompetitionChallenge, item.Challenge, Team = team })
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            return null;

        var attempts = await db.GameplayFacts.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId
                && submission.TeamId == scope.Team.Id
                && submission.CompetitionChallengeId == competitionChallengeId
                && submission.State != GameplayFactState.PlatformFailed)
            .GroupBy(submission => submission.Kind)
            .Select(group => new
            {
                Kind = group.Key,
                Count = group.Count(),
                HasCorrect = group.Any(fact => fact.Result == GameplayFactResult.Correct)
            })
            .ToDictionaryAsync(item => item.Kind, cancellationToken);
        var flagAttempts = attempts.GetValueOrDefault(GameplayFactKind.FlagAttempt);
        var breakAttempts = attempts.GetValueOrDefault(GameplayFactKind.BreakAttempt);
        var fixAttempts = attempts.GetValueOrDefault(GameplayFactKind.FixAttempt);

        return new(
            competitionId,
            scope.Team.Id,
            competitionChallengeId,
            scope.Competition.Mode,
            scope.Competition.ConfigurationJson,
            scope.CompetitionChallenge.RulesJson,
            (flagAttempts?.Count ?? 0) + (breakAttempts?.Count ?? 0),
            fixAttempts?.Count ?? 0,
            scope.Competition.Status,
            scope.Competition.StartAt,
            scope.Competition.EndAt,
            scope.Competition.DeletedAt is not null,
            scope.Challenge.DeletedAt is not null || scope.CompetitionChallenge.DeletedAt is not null,
            scope.CompetitionChallenge.IsPublished,
            scope.Team.DeletedAt is not null,
            scope.Team.IsBanned,
            scope.Team.RegistrationStatus == TeamRegistrationStatus.Approved,
            true,
            breakAttempts?.HasCorrect == true,
            fixAttempts?.HasCorrect == true,
            flagAttempts?.HasCorrect == true);
    }

    public static bool Matches(
        GameplayFactAdmissionSnapshot expected,
        GameplayFactAdmissionSnapshot? current) =>
        current is not null
        && current.CompetitionId == expected.CompetitionId
        && current.TeamId == expected.TeamId
        && current.CompetitionChallengeId == expected.CompetitionChallengeId
        && current.CompetitionStatus == expected.CompetitionStatus
        && current.CompetitionDeleted == expected.CompetitionDeleted
        && current.ChallengeDeleted == expected.ChallengeDeleted
        && current.ChallengePublished == expected.ChallengePublished
        && current.TeamDeleted == expected.TeamDeleted
        && current.TeamBanned == expected.TeamBanned
        && current.TeamApproved == expected.TeamApproved
        && current.UserBelongsToTeam == expected.UserBelongsToTeam
        && current.HasCorrectBreak == expected.HasCorrectBreak
        && current.HasCorrectFix == expected.HasCorrectFix;
}
