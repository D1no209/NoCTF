using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence;

internal static class SubmissionAdmissionPersistence
{
    public static async Task<SubmissionAdmissionSnapshot?> LoadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var scope = await (
            from competition in db.Competitions.AsNoTracking()
            join challenge in db.Challenges.AsNoTracking()
                on competition.Id equals challenge.CompetitionId
            join challengeConfiguration in db.ChallengeConfigurations.AsNoTracking()
                on challenge.Id equals challengeConfiguration.ChallengeId
            join team in db.Teams.AsNoTracking()
                on competition.Id equals team.CompetitionId
            where competition.Id == competitionId && challenge.Id == challengeId && team.Id == teamId
            select new
            {
                Competition = competition,
                Challenge = challenge,
                ChallengeConfiguration = challengeConfiguration,
                Team = team
            }).SingleOrDefaultAsync(ct);
        if (scope is null)
            return null;

        var belongs = await db.Teams.AsNoTracking().AnyAsync(
            team => team.Id == teamId && team.CompetitionId == competitionId
                && team.Members.Any(member => member.UserId == userId), ct);
        var attempts = await db.Submissions.AsNoTracking()
            .Where(submission => submission.CompetitionId == competitionId
                                 && submission.TeamId == teamId
                                 && submission.ChallengeId == challengeId)
            .GroupBy(submission => submission.Kind)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Kind, item => item.Count, ct);
        var challengeInstanceId = scope.Competition.Mode == GameMode.Penetration
            ? await db.ChallengeInstances.AsNoTracking()
                .Where(instance => instance.CompetitionId == competitionId
                                   && instance.ChallengeId == challengeId
                                   && instance.TeamId == teamId
                                   && instance.Status == RuntimeStatus.Running
                                   && (instance.ExpiresAt == null || instance.ExpiresAt > now))
                .OrderByDescending(instance => instance.CreatedAt)
                .Select(instance => (Guid?)instance.Id)
                .FirstOrDefaultAsync(ct)
            : null;

        return new(
            competitionId,
            teamId,
            challengeId,
            scope.Competition.Mode,
            scope.Competition.ConfigurationRevision,
            scope.ChallengeConfiguration.Revision,
            scope.Competition.ConfigurationJson,
            scope.ChallengeConfiguration.Json,
            attempts.GetValueOrDefault(SubmissionKind.Flag),
            attempts.GetValueOrDefault(SubmissionKind.Fix),
            scope.Competition.Status,
            scope.Competition.StartTime,
            scope.Competition.EndTime,
            scope.Competition.Deletion.IsDeleted,
            scope.Challenge.Deletion.IsDeleted,
            scope.Challenge.IsPublished,
            scope.Team.Deletion.IsDeleted,
            scope.Team.Ban.IsBanned,
            scope.Team.RegistrationStatus == TeamRegistrationStatus.Approved,
            belongs,
            challengeInstanceId);
    }

    public static bool Matches(
        SubmissionAdmissionSnapshot expected,
        SubmissionAdmissionSnapshot? current) =>
        current is not null
        && current.Mode == expected.Mode
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
        && current.ChallengeInstanceId == expected.ChallengeInstanceId;
}
