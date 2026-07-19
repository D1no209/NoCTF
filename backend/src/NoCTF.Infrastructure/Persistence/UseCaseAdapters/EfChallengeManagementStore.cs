using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfChallengeManagementStore(NoCtfDbContext db) : IChallengeManagementStore
{
    public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken ct) => db.Competitions.AsNoTracking()
        .Where(x => x.Id == competitionId && !x.Deletion.IsDeleted).Select(x => (CompetitionStatus?)x.Status).SingleOrDefaultAsync(ct);

    public async Task<ChallengeMutationResult> CreateAsync(CreateChallengeCommand command, CancellationToken ct)
    {
        var id = Guid.CreateVersion7(command.CreatedAt);
        var entity = new Challenge { Id = id, CompetitionId = command.CompetitionId, Title = command.Title,
            Description = command.Description?.Trim(), Direction = command.Direction, Order = command.Order,
            CreatedAt = command.CreatedAt, UpdatedAt = command.CreatedAt };
        db.Challenges.Add(entity);
        db.ChallengeConfigurations.Add(new ChallengeConfiguration { ChallengeId = id, Revision = 0, UpdatedAt = command.CreatedAt });
        try { await db.SaveChangesAsync(ct); return new(Map(entity), null); }
        catch (DbUpdateException) { return new(null, "challenge_order_conflict"); }
    }

    public Task<ChallengeView?> FindAsync(Guid competitionId, Guid challengeId, bool includeUnpublished, CancellationToken ct) => Query(includeUnpublished)
        .SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId, ct);
    public async Task<IReadOnlyList<ChallengeView>> ListAsync(Guid competitionId, bool includeUnpublished, CancellationToken ct) =>
        await Query(includeUnpublished).Where(x => x.CompetitionId == competitionId).OrderBy(x => x.Order).ToListAsync(ct);

    public async Task<ChallengeMutationResult> UpdateAsync(UpdateChallengeCommand command, CancellationToken ct)
    {
        if (await IsLocked(command.CompetitionId, ct)) return new(null, "challenge_locked");
        var entity = await db.Challenges.SingleOrDefaultAsync(x => x.Id == command.ChallengeId && x.CompetitionId == command.CompetitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return new(null, "challenge_not_found");
        entity.Title = command.Title; entity.Description = command.Description?.Trim(); entity.Direction = command.Direction;
        entity.Order = command.Order; entity.UpdatedAt = command.UpdatedAt;
        try { await db.SaveChangesAsync(ct); return new(Map(entity), null); }
        catch (DbUpdateException) { return new(null, "challenge_order_conflict"); }
    }

    public async Task<string?> SetPublishedAsync(Guid competitionId, Guid challengeId, bool published, DateTimeOffset now, CancellationToken ct)
    {
        if (await IsLocked(competitionId, ct)) return "challenge_locked";
        var entity = await db.Challenges.SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return "challenge_not_found";
        entity.IsPublished = published; entity.UpdatedAt = now; await db.SaveChangesAsync(ct); return null;
    }

    public async Task<string?> SoftDeleteAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        if (await IsLocked(competitionId, ct)) return "challenge_locked";
        var entity = await db.Challenges.SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return "challenge_not_found";
        entity.Deletion.IsDeleted = true; entity.Deletion.DeletedAt = now; entity.Deletion.DeletedById = actorId; entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct); return null;
    }

    private IQueryable<ChallengeView> Query(bool includeUnpublished) => db.Challenges.AsNoTracking()
        .Where(x => !x.Deletion.IsDeleted && (includeUnpublished || x.IsPublished))
        .Select(x => new ChallengeView(x.Id, x.CompetitionId, x.Title, x.Description, x.Direction, x.Order, x.IsPublished, x.CreatedAt, x.UpdatedAt));
    private Task<bool> IsLocked(Guid competitionId, CancellationToken ct) => db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId
        && (x.Status == CompetitionStatus.Running || x.Status == CompetitionStatus.Paused || x.Status == CompetitionStatus.Finished), ct);
    private static ChallengeView Map(Challenge x) => new(x.Id, x.CompetitionId, x.Title, x.Description, x.Direction, x.Order, x.IsPublished, x.CreatedAt, x.UpdatedAt);
}
