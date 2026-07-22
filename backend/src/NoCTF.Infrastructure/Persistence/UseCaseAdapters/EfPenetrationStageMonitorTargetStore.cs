using Microsoft.EntityFrameworkCore;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfPenetrationStageMonitorTargetStore(NoCtfDbContext db)
    : IPenetrationStageMonitorTargetStore
{
    public async Task<IReadOnlyList<PenetrationStageMonitorTarget>> ListFailedAsync(
        CancellationToken cancellationToken)
    {
        var failed = await db.ChallengeInstances.AsNoTracking()
            .Where(instance => instance.TeamId != null && instance.Status == RuntimeStatus.Failed)
            .Join(db.CompetitionChallenges.AsNoTracking(), instance => instance.CompetitionChallengeId, challenge => challenge.Id,
                (instance, challenge) => new { Instance = instance, Challenge = challenge })
            .Join(db.Competitions.AsNoTracking(), item => item.Instance.CompetitionId, competition => competition.Id,
                (item, competition) => new { item.Instance, item.Challenge, Competition = competition })
            .Join(db.Teams.AsNoTracking(), item => item.Instance.TeamId!.Value, team => team.Id,
                (item, team) => new { item.Instance, item.Challenge, item.Competition, Team = team })
            .Where(item => item.Competition.Mode == GameMode.Penetration
                           && item.Competition.Status == CompetitionStatus.Running
                           && !item.Competition.Deletion.IsDeleted
                           && !item.Challenge.Deletion.IsDeleted
                           && item.Challenge.IsPublished
                           && item.Team.CompetitionId == item.Competition.Id
                           && !item.Team.Deletion.IsDeleted && !item.Team.Ban.IsBanned
                           && item.Team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .OrderBy(item => item.Instance.UpdatedAt).ThenBy(item => item.Instance.Id)
            .Select(item => new
            {
                item.Instance.CompetitionId,
                TeamId = item.Instance.TeamId!.Value,
                CompetitionChallengeId = item.Instance.CompetitionChallengeId,
                InstanceId = item.Instance.Id,
                FailedAt = item.Instance.UpdatedAt,
                item.Challenge.ConfigurationJson
            })
            .ToListAsync(cancellationToken);

        if (failed.Count == 0) return [];
        var competitionIds = failed.Select(item => item.CompetitionId).Distinct().ToArray();
        var completed = await db.Submissions.AsNoTracking()
            .Join(db.ScoringEvents.AsNoTracking(), submission => submission.ScoringEventId,
                scoringEvent => (Guid?)scoringEvent.Id, (submission, scoringEvent) => new { submission, scoringEvent })
            .Where(item => competitionIds.Contains(item.submission.CompetitionId)
                           && item.submission.TeamId != null
                           && item.submission.CompetitionChallengeId != null
                           && item.submission.StageId != null
                           && item.submission.Kind == SubmissionKind.Flag
                           && item.scoringEvent.Result == ScoringResult.Correct
                           && !item.scoringEvent.IsDeleted)
            .Select(item => new
            {
                item.submission.CompetitionId,
                TeamId = item.submission.TeamId!.Value,
                CompetitionChallengeId = item.submission.CompetitionChallengeId!.Value,
                StageId = item.submission.StageId!.Value
            }).ToListAsync(cancellationToken);
        var completedByScope = completed
            .GroupBy(item => (item.CompetitionId, item.TeamId, item.CompetitionChallengeId))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<Guid>)group.Select(item => item.StageId).ToHashSet());

        return failed.Select(item => new PenetrationStageMonitorTarget(
                item.CompetitionId,
                item.TeamId,
                item.CompetitionChallengeId,
                item.InstanceId,
                item.FailedAt,
                item.ConfigurationJson,
                completedByScope.GetValueOrDefault((item.CompetitionId, item.TeamId, item.CompetitionChallengeId))
                ?? new HashSet<Guid>()))
            .ToArray();
    }
}
