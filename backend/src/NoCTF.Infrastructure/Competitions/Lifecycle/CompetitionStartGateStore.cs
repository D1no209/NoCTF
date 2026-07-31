using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Competitions.Lifecycle;

public sealed class CompetitionStartGateStore(NoCtfDbContext db)
    : ICompetitionStartGateStore
{
    public async Task<CompetitionStartGateSnapshot?> LoadAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Id,
                item.Mode,
                item.Status,
                item.ConfigurationJson,
                item.MaxConcurrentRuntimeInstancesPerTeam
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;
        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.DeletedAt == null)
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.ChallengeId,
                template => template.Id,
                (item, template) => new StartGateChallenge(
                    item.Id,
                    template.Mode,
                    item.RulesJson,
                    template.DefinitionJson,
                    item.IsPublished))
            .ToArrayAsync(ct);
        var teams = await db.Teams.AsNoTracking().CountAsync(
            team => team.CompetitionId == competitionId
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved,
            ct);
        return new(
            competition.Id,
            competition.Mode,
            competition.Status,
            competition.ConfigurationJson,
            challenges,
            teams,
            competition.MaxConcurrentRuntimeInstancesPerTeam);
    }
}
