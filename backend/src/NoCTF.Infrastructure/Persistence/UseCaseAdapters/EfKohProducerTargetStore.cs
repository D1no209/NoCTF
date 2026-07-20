using Microsoft.EntityFrameworkCore;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfKohProducerTargetStore(NoCtfDbContext db) : IKohProducerTargetStore
{
    public async Task<IReadOnlyList<KohProducerTarget>> ListRunningAsync(CancellationToken cancellationToken) =>
        await (
                from competition in db.Competitions.AsNoTracking()
                join challenge in db.Challenges.AsNoTracking()
                    on competition.Id equals challenge.CompetitionId
                join challengeConfiguration in db.ChallengeConfigurations.AsNoTracking()
                    on challenge.Id equals challengeConfiguration.ChallengeId
                where competition.Status == CompetitionStatus.Running
                      && competition.Mode == GameMode.Koh
                      && challenge.IsPublished
                orderby competition.Id, challenge.Order, challenge.Id
                select new KohProducerTarget(
                    competition.Id,
                    challenge.Id,
                    competition.StartTime,
                    competition.ConfigurationJson,
                    challengeConfiguration.Json))
            .ToListAsync(cancellationToken);
}
