using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Challenges.Flags;

public sealed class ChallengeFlagManagementStore(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog runtimeTemplates) : IChallengeFlagStore
{
    public ChallengeFlagManagementStore(NoCtfDbContext db)
        : this(db, new NoCTF.GameModes.Registration.ChallengeRuntimeTemplateCatalog()) { }

    public async Task<bool?> SupportsManualStaticFlagsAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(scope, actorId, isAdministrator, ct);
        if (challenge is null)
            return null;
        if (challenge.Mode == GameMode.Awdp)
            return true;
        return challenge.Mode == GameMode.Ctf
            && runtimeTemplates.Get(challenge.Mode, challenge.DefinitionJson)?.FlagSource
                is null or RuntimeFlagSource.Static;
    }

    public async Task<bool?> SupportsRegularExpressionAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(scope, actorId, isAdministrator, ct);
        if (challenge is null)
            return null;
        return challenge.Mode == GameMode.Ctf
            && runtimeTemplates.Get(challenge.Mode, challenge.DefinitionJson)?.FlagSource
                is null or RuntimeFlagSource.Static;
    }

    public async Task<IReadOnlyList<ChallengeFlagView>?> ListAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return null;
        return await Scoped(scope, includeDeleted).AsNoTracking()
            .OrderBy(flag => flag.CreatedAt)
            .ThenBy(flag => flag.Id)
            .Select(flag => new ChallengeFlagView(
                flag.Id, flag.ChallengeId, flag.CompetitionChallengeId, flag.TeamId,
                flag.Flag, flag.MatchKind, flag.SpecificationKind, flag.SpecificationId,
                flag.ValidStart, flag.ValidUntil, flag.DeletedAt, flag.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<ChallengeFlagView?> FindAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return null;
        return await Scoped(scope, includeDeleted).AsNoTracking()
            .Where(flag => flag.Id == flagId)
            .Select(flag => new ChallengeFlagView(
                flag.Id, flag.ChallengeId, flag.CompetitionChallengeId, flag.TeamId,
                flag.Flag, flag.MatchKind, flag.SpecificationKind, flag.SpecificationId,
                flag.ValidStart, flag.ValidUntil, flag.DeletedAt, flag.CreatedAt))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<ChallengeFlagSaveResult> SaveAsync(
        SaveChallengeFlagCommand command,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(command.Scope, actorId, isAdministrator, ct))
            return new(null, ChallengeFlagSaveFailure.ScopeNotFound);
        ChallengeFlag entity;
        if (!command.IsCreate && command.FlagId is Guid flagId)
        {
            entity = await Scoped(command.Scope).SingleOrDefaultAsync(flag => flag.Id == flagId, ct)
                ?? null!;
            if (entity is null)
                return new(null, ChallengeFlagSaveFailure.FlagNotFound);
            if (IsSystemManaged(entity))
                return new(null, ChallengeFlagSaveFailure.SystemManagedFlag);
        }
        else
        {
            if (command.Scope.ChallengeId is Guid challengeId
                && await db.ChallengeFlags.AsNoTracking().AnyAsync(flag =>
                    flag.ChallengeId == challengeId
                    && flag.SpecificationKind == SpecificationKind.Attachment,
                    ct))
            {
                return new(null, ChallengeFlagSaveFailure.DeliveryModeConflict);
            }
            var requestedId = command.FlagId ?? Guid.CreateVersion7(command.Now);
            if (await db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(flag => flag.Id == requestedId, ct))
                return new(null, ChallengeFlagSaveFailure.ResourceIdConflict);
            entity = new ChallengeFlag
            {
                Id = requestedId,
                ChallengeId = command.Scope.ChallengeId,
                CompetitionChallengeId = command.Scope.CompetitionChallengeId,
                CreatedAt = command.Now
            };
            db.ChallengeFlags.Add(entity);
        }
        entity.TeamId = command.TeamId;
        entity.Flag = command.Flag;
        entity.FlagSha256 = ManageChallengeFlags.Hash(command.Flag);
        entity.MatchKind = command.MatchKind;
        entity.SpecificationKind = command.SpecificationKind;
        entity.SpecificationId = command.SpecificationId;
        entity.ValidStart = command.ValidStart;
        entity.ValidUntil = command.ValidUntil;
        try
        {
            await db.SaveChangesAsync(ct);
            return new(Map(entity));
        }
        catch (DbUpdateException) when (command.IsCreate)
        {
            return new(null, ChallengeFlagSaveFailure.ResourceIdConflict);
        }
    }

    public async Task<ChallengeFlagMutationState> DeleteAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return ChallengeFlagMutationState.NotFound;
        var entity = await Scoped(scope).SingleOrDefaultAsync(flag => flag.Id == flagId, ct);
        if (entity is null)
            return ChallengeFlagMutationState.NotFound;
        if (IsSystemManaged(entity))
            return ChallengeFlagMutationState.SystemManagedFlag;
        entity.DeletedAt = now;
        await db.SaveChangesAsync(ct);
        return ChallengeFlagMutationState.Updated;
    }

    public async Task<ChallengeFlagMutationState> RestoreAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return ChallengeFlagMutationState.NotFound;
        var entity = await Scoped(scope, includeDeleted: true)
            .SingleOrDefaultAsync(flag => flag.Id == flagId && flag.DeletedAt != null, ct);
        if (entity is null)
            return ChallengeFlagMutationState.NotFound;
        if (IsSystemManaged(entity))
            return ChallengeFlagMutationState.SystemManagedFlag;
        if (entity.MatchKind == ChallengeFlagMatchKind.RegularExpression
            && await SupportsRegularExpressionAsync(scope, actorId, isAdministrator, ct) != true)
            return ChallengeFlagMutationState.NotFound;
        entity.DeletedAt = null;
        await db.SaveChangesAsync(ct);
        return ChallengeFlagMutationState.Updated;
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

    private async Task<ChallengeFlagSupport?> LoadChallengeAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(scope, actorId, isAdministrator, ct))
            return null;
        return scope.ChallengeId is Guid challengeId
            ? await db.Challenges.AsNoTracking()
                .Where(item => item.Id == challengeId)
                .Select(item => new ChallengeFlagSupport(item.Mode, item.DefinitionJson))
                .SingleAsync(ct)
            : await db.CompetitionChallenges.AsNoTracking()
                .Where(item => item.Id == scope.CompetitionChallengeId
                    && item.CompetitionId == scope.CompetitionId)
                .Join(
                    db.Challenges.AsNoTracking(),
                    item => item.ChallengeId,
                    template => template.Id,
                    (_, template) => new ChallengeFlagSupport(template.Mode, template.DefinitionJson))
                .SingleAsync(ct);
    }

    private static ChallengeFlagView Map(ChallengeFlag flag) =>
        new(
            flag.Id, flag.ChallengeId, flag.CompetitionChallengeId, flag.TeamId,
            flag.Flag, flag.MatchKind, flag.SpecificationKind, flag.SpecificationId,
            flag.ValidStart, flag.ValidUntil, flag.DeletedAt, flag.CreatedAt);

    private static bool IsSystemManaged(ChallengeFlag flag) =>
        flag.TeamId is not null
        || flag.SpecificationKind is not null
        || flag.SpecificationId is not null
        || flag.ValidStart is not null
        || flag.ValidUntil is not null;

    private sealed record ChallengeFlagSupport(GameMode Mode, string DefinitionJson);

}
