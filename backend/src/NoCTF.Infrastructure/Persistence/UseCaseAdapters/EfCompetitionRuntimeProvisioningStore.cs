using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionRuntimeProvisioningStore(NoCtfDbContext db)
    : ICompetitionRuntimeProvisioningStore
{
    public async Task<CompetitionRuntimeProvisioningSnapshot?> LoadAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId)
            .Select(item => new { item.Id, item.Mode, item.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null) return null;

        var challenges = await (
                from challenge in db.Challenges.AsNoTracking()
                join configuration in db.ChallengeConfigurations.AsNoTracking()
                    on challenge.Id equals configuration.ChallengeId
                where challenge.CompetitionId == competitionId && challenge.IsPublished
                orderby challenge.Order, challenge.Id
                select new RuntimeChallengeDefinition(
                    challenge.Id,
                    challenge.Order,
                    configuration.Revision,
                    configuration.Json))
            .ToListAsync(cancellationToken);
        var teamIds = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                           && team.RegistrationStatus == TeamRegistrationStatus.Approved
                           && !team.Ban.IsBanned)
            .OrderBy(team => team.Id)
            .Select(team => team.Id)
            .ToListAsync(cancellationToken);
        return new(competition.Id, competition.Mode, competition.Status, challenges, teamIds);
    }
}
