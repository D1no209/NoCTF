using Microsoft.EntityFrameworkCore;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfAwdCheckerTargetStore(NoCtfDbContext db) : IAwdCheckerTargetStore
{
    public async Task<IReadOnlyList<AwdCheckerTarget>> ListRunningAsync(CancellationToken cancellationToken)
    {
        var rows = await db.ChallengeInstances.AsNoTracking()
            .Join(db.Competitions.AsNoTracking(), instance => instance.CompetitionId, competition => competition.Id,
                (instance, competition) => new { Instance = instance, Competition = competition })
            .Join(db.CompetitionChallenges.AsNoTracking(), item => item.Instance.CompetitionChallengeId, challenge => challenge.Id,
                (item, challenge) => new { item.Instance, item.Competition, Challenge = challenge })
            .Join(db.Teams.AsNoTracking(), item => item.Instance.TeamId, team => (Guid?)team.Id,
                (item, team) => new { item.Instance, item.Competition, item.Challenge, Team = team })
            .Where(item => item.Competition.Mode == GameMode.Awd && item.Competition.Status == CompetitionStatus.Running
                           && item.Instance.Status == RuntimeStatus.Running && item.Instance.TeamId != null
                           && item.Instance.EntryUrl != null && item.Challenge.IsPublished && !item.Challenge.Deletion.IsDeleted
                           && item.Team.RegistrationStatus == TeamRegistrationStatus.Approved
                           && !item.Team.Deletion.IsDeleted && !item.Team.Ban.IsBanned)
            .OrderBy(item => item.Competition.Id).ThenBy(item => item.Challenge.Id).ThenBy(item => item.Team.Id)
            .Select(item => new
            {
                item.Competition.Id,
                CompetitionChallengeId = item.Challenge.Id,
                TeamId = item.Team.Id,
                item.Competition.StartTime,
                CompetitionConfigurationJson = item.Competition.ConfigurationJson,
                ChallengeConfigurationJson = item.Challenge.ConfigurationJson,
                ChallengeRevision = item.Challenge.Revision,
                EntryUrl = item.Instance.EntryUrl!
            })
            .ToListAsync(cancellationToken);
        return rows.Select(item => new AwdCheckerTarget(item.Id, item.CompetitionChallengeId, item.TeamId,
            item.StartTime, item.CompetitionConfigurationJson, item.ChallengeConfigurationJson,
            new Uri(item.EntryUrl), item.ChallengeRevision)).ToList();
    }
}
