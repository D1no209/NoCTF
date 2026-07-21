using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using Npgsql;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfChallengeManagementStore(NoCtfDbContext db) : IChallengeManagementStore
{
    public Task<ChallengeCompetitionContext?> GetCompetitionAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking()
            .Where(x => x.Id == competitionId && !x.Deletion.IsDeleted)
            .Select(x => new ChallengeCompetitionContext(x.Mode, x.Status))
            .SingleOrDefaultAsync(ct);

    public async Task<ChallengeMutationResult> CreateAsync(
        CreateChallengeCommand command,
        string configurationJson,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status is null) return new(null, ChallengeMutationFailure.CompetitionNotFound);
        if (ChallengeMutationPolicy.IsLocked(status.Value)) return new(null, ChallengeMutationFailure.ChallengeLocked);
        var id = Guid.CreateVersion7(command.CreatedAt);
        var entity = new Challenge { Id = id, CompetitionId = command.CompetitionId, Title = command.Title,
            Description = command.Description?.Trim(), Direction = command.Direction, Order = command.Order,
            CreatedAt = command.CreatedAt, UpdatedAt = command.CreatedAt };
        db.Challenges.Add(entity);
        db.ChallengeConfigurations.Add(new ChallengeConfiguration
        {
            ChallengeId = id,
            Json = configurationJson,
            Revision = 0,
            UpdatedAt = command.CreatedAt
        });
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return new(Map(entity)); }
        catch (DbUpdateException exception) when (IsChallengeOrderConflict(exception))
        {
            return new(null, ChallengeMutationFailure.ChallengeOrderConflict);
        }
    }

    public Task<ChallengeView?> FindAsync(Guid competitionId, Guid challengeId, bool includeUnpublished, CancellationToken ct) => Query(includeUnpublished)
        .SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId, ct);
    public async Task<IReadOnlyList<ChallengeView>> ListAsync(Guid competitionId, bool includeUnpublished, CancellationToken ct) =>
        await Query(includeUnpublished).Where(x => x.CompetitionId == competitionId).OrderBy(x => x.Order).ToListAsync(ct);

    public async Task<ChallengeMutationResult> UpdateAsync(UpdateChallengeCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status is null) return new(null, ChallengeMutationFailure.CompetitionNotFound);
        if (ChallengeMutationPolicy.IsLocked(status.Value)) return new(null, ChallengeMutationFailure.ChallengeLocked);
        var entity = await db.Challenges.SingleOrDefaultAsync(x => x.Id == command.ChallengeId && x.CompetitionId == command.CompetitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return new(null, ChallengeMutationFailure.ChallengeNotFound);
        entity.Title = command.Title; entity.Description = command.Description?.Trim(); entity.Direction = command.Direction;
        entity.Order = command.Order; entity.UpdatedAt = command.UpdatedAt;
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return new(Map(entity)); }
        catch (DbUpdateException exception) when (IsChallengeOrderConflict(exception))
        {
            return new(null, ChallengeMutationFailure.ChallengeOrderConflict);
        }
    }

    public async Task<ChallengeMutationFailure?> SetPublishedAsync(Guid competitionId, Guid challengeId, bool published, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return ChallengeMutationFailure.CompetitionNotFound;
        if (ChallengeMutationPolicy.IsLocked(status.Value)) return ChallengeMutationFailure.ChallengeLocked;
        var entity = await db.Challenges.SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return ChallengeMutationFailure.ChallengeNotFound;
        entity.IsPublished = published; entity.UpdatedAt = now; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
    }

    public async Task<ChallengeMutationFailure?> SoftDeleteAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return ChallengeMutationFailure.CompetitionNotFound;
        if (ChallengeMutationPolicy.IsLocked(status.Value)) return ChallengeMutationFailure.ChallengeLocked;
        var entity = await db.Challenges.SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return ChallengeMutationFailure.ChallengeNotFound;
        entity.Deletion.IsDeleted = true; entity.Deletion.DeletedAt = now; entity.Deletion.DeletedById = actorId; entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
    }

    private IQueryable<ChallengeView> Query(bool includeUnpublished) => db.Challenges.AsNoTracking()
        .Where(x => !x.Deletion.IsDeleted && (includeUnpublished || x.IsPublished))
        .Select(x => new ChallengeView(x.Id, x.CompetitionId, x.Title, x.Description, x.Direction, x.Order, x.IsPublished, x.CreatedAt, x.UpdatedAt));
    private static ChallengeView Map(Challenge x) => new(x.Id, x.CompetitionId, x.Title, x.Description, x.Direction, x.Order, x.IsPublished, x.CreatedAt, x.UpdatedAt);
    private static bool IsChallengeOrderConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_challenges_CompetitionId_Order"
        };
}
