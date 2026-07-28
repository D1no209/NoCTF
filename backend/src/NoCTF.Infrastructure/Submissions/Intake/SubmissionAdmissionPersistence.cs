using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Submissions.Intake;

internal static class SubmissionAdmissionPersistence
{
    public static async Task<SubmissionAdmissionSnapshot?> LoadAsync(
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

        var attempts = await db.Submissions.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId
                && submission.TeamId == scope.Team.Id
                && submission.CompetitionChallengeId == competitionChallengeId
                && submission.EvaluationState != SubmissionEvaluationState.PlatformFailed)
            .GroupBy(submission => submission.Kind)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Kind, item => item.Count, cancellationToken);

        var hasCorrectBreak = scope.Competition.Mode == GameMode.Awdp
            && await db.ScoringEvents.AsNoTracking()
                .Where(scoringEvent =>
                    scoringEvent.CompetitionId == competitionId
                    && scoringEvent.TeamId == scope.Team.Id
                    && scoringEvent.CompetitionChallengeId == competitionChallengeId
                    && scoringEvent.Result == ScoringResult.Correct
                    && scoringEvent.SubmissionId != null)
                .Join(
                    db.Submissions.AsNoTracking()
                        .Where(submission => submission.Kind == SubmissionKind.Break),
                    scoringEvent => scoringEvent.SubmissionId,
                    submission => (Guid?)submission.Id,
                    (_, _) => true)
                .AnyAsync(cancellationToken);

        return new(
            competitionId,
            scope.Team.Id,
            competitionChallengeId,
            scope.Competition.Mode,
            scope.Competition.ConfigurationRevision,
            scope.CompetitionChallenge.Revision,
            scope.Competition.ConfigurationJson,
            scope.CompetitionChallenge.ConfigurationJson,
            attempts.GetValueOrDefault(SubmissionKind.Flag)
                + attempts.GetValueOrDefault(SubmissionKind.Break),
            attempts.GetValueOrDefault(SubmissionKind.Fix),
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
            hasCorrectBreak);
    }

    public static bool Matches(
        SubmissionAdmissionSnapshot expected,
        SubmissionAdmissionSnapshot? current) =>
        current is not null
        && current.CompetitionId == expected.CompetitionId
        && current.TeamId == expected.TeamId
        && current.CompetitionChallengeId == expected.CompetitionChallengeId
        && current.CompetitionConfigurationRevision == expected.CompetitionConfigurationRevision
        && current.ChallengeConfigurationRevision == expected.ChallengeConfigurationRevision
        && current.CompetitionStatus == expected.CompetitionStatus
        && current.CompetitionDeleted == expected.CompetitionDeleted
        && current.ChallengeDeleted == expected.ChallengeDeleted
        && current.ChallengePublished == expected.ChallengePublished
        && current.TeamDeleted == expected.TeamDeleted
        && current.TeamBanned == expected.TeamBanned
        && current.TeamApproved == expected.TeamApproved
        && current.UserBelongsToTeam == expected.UserBelongsToTeam
        && current.HasCorrectBreak == expected.HasCorrectBreak;
}
