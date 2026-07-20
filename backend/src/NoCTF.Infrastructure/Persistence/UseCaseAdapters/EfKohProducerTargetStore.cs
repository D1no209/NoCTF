using Microsoft.EntityFrameworkCore;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfKohProducerTargetStore(NoCtfDbContext db) : IKohProducerTargetStore
{
    public async Task<IReadOnlyList<KohProducerTarget>> ListRunningAsync(CancellationToken cancellationToken) =>
        await db.Competitions.AsNoTracking()
            .Join(db.CompetitionChallenges.AsNoTracking(), competition => competition.Id, challenge => challenge.CompetitionId,
                (competition, challenge) => new { Competition = competition, CompetitionChallenge = challenge })
            .Join(db.Challenges.AsNoTracking(), item => item.CompetitionChallenge.ChallengeId, template => template.Id,
                (item, template) => new { item.Competition, item.CompetitionChallenge, Template = template })
            .Where(item => item.Competition.Status == CompetitionStatus.Running
                           && item.Competition.Mode == GameMode.Koh
                           && item.CompetitionChallenge.IsPublished
                           && !item.CompetitionChallenge.Deletion.IsDeleted
                           && !item.Template.Deletion.IsDeleted)
            .OrderBy(item => item.Competition.Id)
            .ThenBy(item => item.CompetitionChallenge.Order)
            .ThenBy(item => item.CompetitionChallenge.Id)
            .Select(item => new KohProducerTarget(
                item.Competition.Id,
                item.CompetitionChallenge.Id,
                item.Competition.StartTime,
                item.Competition.ConfigurationJson,
                item.CompetitionChallenge.ConfigurationJson))
            .ToListAsync(cancellationToken);
}
