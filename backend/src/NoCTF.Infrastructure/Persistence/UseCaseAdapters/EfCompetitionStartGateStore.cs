using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionStartGateStore(NoCtfDbContext db)
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
                item.ConfigurationJson
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;
        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.DeletedAt == null)
            .Select(item => new StartGateChallenge(
                item.Id,
                item.ConfigurationJson,
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
            teams);
    }
}
