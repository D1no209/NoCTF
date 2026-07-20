using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence;

internal static class SubmissionAdmissionPersistence
{
    public static async Task<SubmissionAdmissionSnapshot?> LoadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        CancellationToken ct)
    {
        var scope = await (
            from competition in db.Competitions.AsNoTracking()
            join competitionConfiguration in db.CompetitionConfigurations.AsNoTracking()
                on competition.Id equals competitionConfiguration.CompetitionId
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
                CompetitionConfiguration = competitionConfiguration,
                Challenge = challenge,
                ChallengeConfiguration = challengeConfiguration,
                Team = team
            }).SingleOrDefaultAsync(ct);
        if (scope is null)
            return null;

        var belongs = await db.TeamMembers.AsNoTracking().AnyAsync(
            member => member.CompetitionId == competitionId
                      && member.TeamId == teamId
                      && member.UserId == userId,
            ct);
        var attempts = await db.Submissions.AsNoTracking()
            .Where(submission => submission.CompetitionId == competitionId
                                 && submission.TeamId == teamId
                                 && submission.ChallengeId == challengeId)
            .GroupBy(submission => submission.Kind)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Kind, item => item.Count, ct);

        return new(
            competitionId,
            teamId,
            challengeId,
            scope.Competition.Mode,
            scope.CompetitionConfiguration.Revision,
            scope.ChallengeConfiguration.Revision,
            scope.CompetitionConfiguration.Json,
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
            belongs);
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
        && current.UserBelongsToTeam == expected.UserBelongsToTeam;
}
