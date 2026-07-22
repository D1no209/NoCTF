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
        var template = new Challenge { Id = Guid.CreateVersion7(command.CreatedAt), Title = command.Title,
            Description = command.Description?.Trim(), Direction = command.Direction,
            CreatedAt = command.CreatedAt, UpdatedAt = command.CreatedAt };
        var entity = new CompetitionChallenge
        {
            Id = Guid.CreateVersion7(command.CreatedAt.AddTicks(1)),
            CompetitionId = command.CompetitionId,
            ChallengeId = template.Id,
            BaseScore = 0,
            Order = command.Order,
            ConfigurationJson = configurationJson,
            Revision = 0,
            UpdatedAt = command.CreatedAt
        };
        db.Challenges.Add(template);
        db.CompetitionChallenges.Add(entity);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return new(Map(entity, template)); }
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
        var entity = await db.CompetitionChallenges
            .Join(db.Challenges, instance => instance.ChallengeId, template => template.Id,
                (instance, template) => new { Instance = instance, Template = template })
            .SingleOrDefaultAsync(item => item.Instance.Id == command.ChallengeId
                && item.Instance.CompetitionId == command.CompetitionId && !item.Instance.Deletion.IsDeleted, ct);
        if (entity is null) return new(null, ChallengeMutationFailure.ChallengeNotFound);
        entity.Template.Title = command.Title; entity.Template.Description = command.Description?.Trim();
        entity.Template.Direction = command.Direction; entity.Template.UpdatedAt = command.UpdatedAt;
        entity.Instance.Order = command.Order; entity.Instance.UpdatedAt = command.UpdatedAt;
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return new(Map(entity.Instance, entity.Template)); }
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
        var entity = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return ChallengeMutationFailure.ChallengeNotFound;
        entity.IsPublished = published; entity.UpdatedAt = now; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
    }

    public async Task<ChallengeMutationFailure?> SoftDeleteAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return ChallengeMutationFailure.CompetitionNotFound;
        if (ChallengeMutationPolicy.IsLocked(status.Value)) return ChallengeMutationFailure.ChallengeLocked;
        var entity = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return ChallengeMutationFailure.ChallengeNotFound;
        entity.Deletion.IsDeleted = true; entity.Deletion.DeletedAt = now; entity.Deletion.DeletedById = actorId; entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
    }

    private IQueryable<ChallengeView> Query(bool includeUnpublished) => db.CompetitionChallenges.AsNoTracking()
        .Join(db.Challenges.AsNoTracking(), instance => instance.ChallengeId, template => template.Id,
            (instance, template) => new { Instance = instance, Template = template })
        .Where(item => !item.Instance.Deletion.IsDeleted && !item.Template.Deletion.IsDeleted
                       && (includeUnpublished || item.Instance.IsPublished))
        .Select(item => new ChallengeView(item.Instance.Id, item.Instance.CompetitionId, item.Template.Title,
            item.Template.Description, item.Template.Direction, item.Instance.Order, item.Instance.IsPublished,
            item.Template.CreatedAt, item.Instance.UpdatedAt));
    private static ChallengeView Map(CompetitionChallenge instance, Challenge template) => new(instance.Id,
        instance.CompetitionId, template.Title, template.Description, template.Direction, instance.Order,
        instance.IsPublished, template.CreatedAt, instance.UpdatedAt);
    private static bool IsChallengeOrderConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_challenges_CompetitionId_Order"
        };
}
