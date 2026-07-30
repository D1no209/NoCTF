using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Challenges;
using Npgsql;

namespace NoCTF.Infrastructure.Challenges.Management;

public sealed class ChallengeManagementStore(NoCtfDbContext db) : IChallengeManagementStore
{
    public Task<ChallengeCompetitionContext?> GetCompetitionAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => new ChallengeCompetitionContext(competition.Mode, competition.Status))
            .SingleOrDefaultAsync(ct);

    public async Task<ChallengeMutationResult> CreateAsync(
        CreateCompetitionChallengeCommand command,
        string configurationJson,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct) is null)
            return new(null, ChallengeMutationFailure.CompetitionNotFound);
        var competitionMode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == command.CompetitionId)
            .Select(competition => competition.Mode)
            .SingleAsync(ct);
        if (command.CompetitionChallengeId is Guid requestedId
            && await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.Id == requestedId, ct))
            return new(null, ChallengeMutationFailure.ResourceIdConflict);

        var template = await db.Challenges.AsNoTracking()
            .SingleOrDefaultAsync(challenge => challenge.Id == command.ChallengeId, ct);
        if (template is null)
            return new(null, ChallengeMutationFailure.TemplateNotFound);
        if (template.Mode != competitionMode)
            return new(null, ChallengeMutationFailure.TemplateModeMismatch);

        var entity = new CompetitionChallenge
        {
            Id = command.CompetitionChallengeId ?? Guid.CreateVersion7(command.CreatedAt),
            CompetitionId = command.CompetitionId,
            ChallengeId = command.ChallengeId,
            BaseScore = command.BaseScore,
            Order = command.Order,
            RulesJson = configurationJson,
            UpdatedAt = command.CreatedAt
        };
        db.CompetitionChallenges.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            await LeaderboardRevision.IncrementAsync(db, command.CompetitionId, ct);
            await transaction.CommitAsync(ct);
            return new(Map(entity, template));
        }
        catch (DbUpdateException exception) when (IsCompetitionChallengeConflict(exception))
        {
            return new(null, ChallengeMutationFailure.ChallengeOrderConflict);
        }
    }

    public Task<ChallengeView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken ct) =>
        Query(
                includeUnpublished,
                includeDeleted,
                competitionId,
                competitionChallengeId)
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ChallengeView>> ListAsync(
        Guid competitionId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken ct) =>
        await Query(includeUnpublished, includeDeleted, competitionId)
            .ToListAsync(ct);

    public async Task<ChallengeMutationResult> UpdateAsync(
        UpdateCompetitionChallengeCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct) is null)
            return new(null, ChallengeMutationFailure.CompetitionNotFound);

        var entity = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == command.CompetitionChallengeId &&
                item.CompetitionId == command.CompetitionId, ct);
        if (entity is null)
            return new(null, ChallengeMutationFailure.ChallengeNotFound);
        if (entity.Revision != command.ExpectedRevision)
            return new(null, ChallengeMutationFailure.RevisionConflict);

        entity.BaseScore = command.BaseScore;
        entity.Order = command.Order;
        entity.IsPublished = command.IsPublished;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = command.UpdatedAt;
        try
        {
            await db.SaveChangesAsync(ct);
            await LeaderboardRevision.IncrementAsync(db, command.CompetitionId, ct);
            await transaction.CommitAsync(ct);
            var template = await db.Challenges.AsNoTracking()
                .SingleAsync(challenge => challenge.Id == entity.ChallengeId, ct);
            return new(Map(entity, template));
        }
        catch (DbUpdateException exception) when (IsCompetitionChallengeConflict(exception))
        {
            return new(null, ChallengeMutationFailure.ChallengeOrderConflict);
        }
    }

    public Task<ChallengeMutationFailure?> SoftDeleteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken ct) =>
        SetDeletedAsync(competitionId, competitionChallengeId, now, restore: false, ct);

    public Task<ChallengeMutationFailure?> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken ct) =>
        SetDeletedAsync(competitionId, competitionChallengeId, now, restore: true, ct);

    private async Task<ChallengeMutationFailure?> SetDeletedAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        bool restore,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionWriteLock.AcquireAsync(db, competitionId, ct) is null)
            return ChallengeMutationFailure.CompetitionNotFound;

        var query = restore
            ? db.CompetitionChallenges.IgnoreQueryFilters()
            : db.CompetitionChallenges;
        var entity = await query.SingleOrDefaultAsync(item =>
            item.Id == competitionChallengeId &&
            item.CompetitionId == competitionId &&
            (restore ? item.DeletedAt != null : item.DeletedAt == null), ct);
        if (entity is null)
            return ChallengeMutationFailure.ChallengeNotFound;

        entity.DeletedAt = restore ? null : now;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
            await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        catch (DbUpdateException exception) when (IsCompetitionChallengeConflict(exception))
        {
            return ChallengeMutationFailure.ChallengeOrderConflict;
        }
    }

    private IQueryable<ChallengeView> Query(
        bool includeUnpublished,
        bool includeDeleted,
        Guid? competitionId = null,
        Guid? competitionChallengeId = null)
    {
        var instances = includeDeleted
            ? db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            : db.CompetitionChallenges.AsNoTracking();
        var templates = includeDeleted
            ? db.Challenges.IgnoreQueryFilters().AsNoTracking()
            : db.Challenges.AsNoTracking();
        if (competitionId is Guid actualCompetitionId)
            instances = instances.Where(item => item.CompetitionId == actualCompetitionId);
        if (competitionChallengeId is Guid actualCompetitionChallengeId)
            instances = instances.Where(item => item.Id == actualCompetitionChallengeId);
        return instances
            .Join(
                templates,
                instance => instance.ChallengeId,
                template => template.Id,
                (instance, template) => new { Instance = instance, Template = template })
            .Where(item => includeUnpublished || item.Instance.IsPublished)
            .OrderBy(item => item.Instance.Order)
            .ThenBy(item => item.Instance.Id)
            .Select(item => new ChallengeView(
                item.Instance.Id,
                item.Instance.CompetitionId,
                item.Instance.ChallengeId,
                item.Template.Title,
                item.Template.Description,
                item.Template.Direction,
                item.Instance.BaseScore,
                item.Instance.Order,
                item.Instance.IsPublished,
                item.Instance.Revision,
                item.Instance.DeletedAt,
                item.Template.CreatedAt,
                item.Instance.UpdatedAt));
    }

    private static ChallengeView Map(CompetitionChallenge instance, Challenge template) =>
        new(
            instance.Id,
            instance.CompetitionId,
            instance.ChallengeId,
            template.Title,
            template.Description,
            template.Direction,
            instance.BaseScore,
            instance.Order,
            instance.IsPublished,
            instance.Revision,
            instance.DeletedAt,
            template.CreatedAt,
            instance.UpdatedAt);

    private static bool IsCompetitionChallengeConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
}
