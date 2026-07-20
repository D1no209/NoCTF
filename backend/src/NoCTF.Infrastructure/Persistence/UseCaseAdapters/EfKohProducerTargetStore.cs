using Microsoft.EntityFrameworkCore;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfKohProducerTargetStore(NoCtfDbContext db) : IKohProducerTargetStore
{
    public async Task<IReadOnlyList<KohProducerTarget>> ListRunningAsync(CancellationToken cancellationToken) =>
        await db.Competitions.AsNoTracking()
            .Join(db.Challenges.AsNoTracking(), competition => competition.Id, challenge => challenge.CompetitionId,
                (competition, challenge) => new { Competition = competition, Challenge = challenge })
            .Join(db.ChallengeConfigurations.AsNoTracking(), item => item.Challenge.Id, configuration => configuration.ChallengeId,
                (item, configuration) => new { item.Competition, item.Challenge, Configuration = configuration })
            .Where(item => item.Competition.Status == CompetitionStatus.Running
                           && item.Competition.Mode == GameMode.Koh
                           && item.Challenge.IsPublished)
            .OrderBy(item => item.Competition.Id)
            .ThenBy(item => item.Challenge.Order)
            .ThenBy(item => item.Challenge.Id)
            .Select(item => new KohProducerTarget(
                item.Competition.Id,
                item.Challenge.Id,
                item.Competition.StartTime,
                item.Competition.ConfigurationJson,
                item.Configuration.Json))
            .ToListAsync(cancellationToken);
}
