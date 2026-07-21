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

        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Join(db.Challenges.AsNoTracking(), challenge => challenge.ChallengeId, template => template.Id,
                (challenge, template) => new { CompetitionChallenge = challenge, Template = template })
            .Where(item => item.CompetitionChallenge.CompetitionId == competitionId
                           && item.CompetitionChallenge.IsPublished
                           && !item.Template.Deletion.IsDeleted)
            .OrderBy(item => item.CompetitionChallenge.Order)
            .ThenBy(item => item.CompetitionChallenge.Id)
            .Select(item => new RuntimeChallengeDefinition(
                item.CompetitionChallenge.Id,
                item.CompetitionChallenge.Order,
                item.CompetitionChallenge.Revision,
                item.CompetitionChallenge.ConfigurationJson))
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
