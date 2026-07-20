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

        var challenges = await db.Challenges.AsNoTracking()
            .Join(db.ChallengeConfigurations.AsNoTracking(), challenge => challenge.Id, configuration => configuration.ChallengeId,
                (challenge, configuration) => new { Challenge = challenge, Configuration = configuration })
            .Where(item => item.Challenge.CompetitionId == competitionId && item.Challenge.IsPublished)
            .OrderBy(item => item.Challenge.Order)
            .ThenBy(item => item.Challenge.Id)
            .Select(item => new RuntimeChallengeDefinition(
                item.Challenge.Id,
                item.Challenge.Order,
                item.Configuration.Revision,
                item.Configuration.Json))
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
