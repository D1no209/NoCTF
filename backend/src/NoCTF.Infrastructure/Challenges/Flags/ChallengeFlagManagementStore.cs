using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Challenges.Flags;

public sealed class ChallengeFlagManagementStore(NoCtfDbContext db) : IChallengeFlagStore
{
    public async Task<IReadOnlyList<ChallengeFlagView>?> ListAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct,
        bool includeDeleted = false)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return null;
        return await Scoped(scope, includeDeleted).AsNoTracking()
            .OrderBy(flag => flag.CreatedAt)
            .ThenBy(flag => flag.Id)
            .Select(flag => new ChallengeFlagView(
                flag.Id, flag.ChallengeId, flag.CompetitionChallengeId, flag.TeamId,
                flag.Flag, flag.SpecificationKind, flag.SpecificationId,
                flag.ValidStart, flag.ValidUntil, flag.CreatedAt, flag.DeletedAt))
            .ToListAsync(ct);
    }

    public async Task<ChallengeFlagView?> FindAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct,
        bool includeDeleted = false)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return null;
        return await Scoped(scope, includeDeleted).AsNoTracking()
            .Where(flag => flag.Id == flagId)
            .Select(flag => new ChallengeFlagView(
                flag.Id, flag.ChallengeId, flag.CompetitionChallengeId, flag.TeamId,
                flag.Flag, flag.SpecificationKind, flag.SpecificationId,
                flag.ValidStart, flag.ValidUntil, flag.CreatedAt, flag.DeletedAt))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<ChallengeFlagView?> SaveAsync(
        SaveChallengeFlagCommand command,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(command.Scope, actorId, isAdministrator, ct))
            return null;
        ChallengeFlag entity;
        if (command.FlagId is Guid flagId)
        {
            entity = await Scoped(command.Scope).SingleOrDefaultAsync(flag => flag.Id == flagId, ct)
                ?? null!;
            if (entity is null)
                return null;
        }
        else
        {
            entity = new ChallengeFlag
            {
                Id = command.RequestedId ?? Guid.CreateVersion7(command.Now),
                ChallengeId = command.Scope.ChallengeId,
                CompetitionChallengeId = command.Scope.CompetitionChallengeId,
                CreatedAt = command.Now
            };
            db.ChallengeFlags.Add(entity);
        }
        entity.TeamId = command.TeamId;
        entity.Flag = command.Flag;
        entity.FlagSha256 = ManageChallengeFlags.Hash(command.Flag);
        entity.SpecificationKind = command.SpecificationKind;
        entity.SpecificationId = command.SpecificationId;
        entity.ValidStart = command.ValidStart;
        entity.ValidUntil = command.ValidUntil;
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return false;
        var entity = await Scoped(scope).SingleOrDefaultAsync(flag => flag.Id == flagId, ct);
        if (entity is null)
            return false;
        entity.DeletedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RestoreAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return false;
        var entity = await Scoped(scope, includeDeleted: true)
            .SingleOrDefaultAsync(flag => flag.Id == flagId && flag.DeletedAt != null, ct);
        if (entity is null)
            return false;
        entity.DeletedAt = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<ChallengeFlag> Scoped(
        ChallengeFlagScope scope,
        bool includeDeleted = false)
    {
        var source = includeDeleted
            ? db.ChallengeFlags.IgnoreQueryFilters()
            : db.ChallengeFlags;
        return source.Where(flag =>
            flag.ChallengeId == scope.ChallengeId &&
            flag.CompetitionChallengeId == scope.CompetitionChallengeId);
    }

    private Task<bool> ScopeExistsAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        if (scope.ChallengeId is Guid challengeId)
            return db.Challenges.AnyAsync(challenge =>
                challenge.Id == challengeId &&
                (isAdministrator ||
                 challenge.OwnerId == actorId ||
                 (actorId != null && challenge.ManagerIds.Contains(actorId.Value))), ct);
        return db.CompetitionChallenges.AnyAsync(
            challenge =>
                challenge.Id == scope.CompetitionChallengeId &&
                challenge.CompetitionId == scope.CompetitionId, ct);
    }

    private static ChallengeFlagView Map(ChallengeFlag flag) =>
        new(
            flag.Id, flag.ChallengeId, flag.CompetitionChallengeId, flag.TeamId,
            flag.Flag, flag.SpecificationKind, flag.SpecificationId,
            flag.ValidStart, flag.ValidUntil, flag.CreatedAt, flag.DeletedAt);

}
