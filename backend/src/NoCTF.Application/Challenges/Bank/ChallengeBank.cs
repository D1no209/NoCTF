using NoCTF.Application.Common;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Bank;

public sealed record ChallengeTemplateView(
    Guid Id,
    Guid OwnerId,
    IReadOnlyList<Guid> ManagerIds,
    GameMode Mode,
    ChallengeVisibility Visibility,
    string Title,
    string? Description,
    string Direction,
    string DefinitionJson,
    DateTimeOffset? DeletedAt,
    int ActiveCompetitionReferenceCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public CtfInteractionKind InteractionKind => Mode == GameMode.Ctf
        ? CtfInteractionDefinition.Parse(DefinitionJson)
        : CtfInteractionKind.FlagSubmission;
}

public sealed record ChallengeTemplateListQuery(
    Guid ActorId,
    bool IsAdministrator,
    bool IncludeDeleted,
    string? Keyword,
    string? Direction,
    int Offset,
    int Limit,
    bool Desc);

public sealed record ChallengeTemplateListPage(
    IReadOnlyList<ChallengeTemplateView> Items,
    int Total,
    IReadOnlyList<string> Directions);

public enum ChallengeTemplateWriteState
{
    Succeeded,
    InvalidRequest,
    InvalidDefinition,
    ResourceIdConflict,
    NotFoundOrForbidden,
    ActiveCompetitionModeConflict,
    ActiveRuntimeDefinitionConflict,
    OwnerIncludedInManagerSet,
    UserNotFound,
    RoleNotEligible,
    ExperimentalFeatureDisabled,
    InteractionKindConflict
}

public sealed record ChallengeTemplateWriteResult(
    ChallengeTemplateWriteState State,
    ChallengeTemplateView? Template = null,
    IReadOnlyList<Guid>? UserIds = null,
    string? Detail = null)
{
    public bool Succeeded => State == ChallengeTemplateWriteState.Succeeded;
}

public sealed record CreateChallengeTemplateCommand(
    Guid? ChallengeId,
    Guid OwnerId,
    GameMode Mode,
    ChallengeVisibility Visibility,
    string Title,
    string? Description,
    string Direction,
    string DefinitionJson,
    DateTimeOffset CreatedAt);

public sealed record UpdateChallengeTemplateCommand(
    Guid ChallengeId,
    Guid ActorId,
    bool IsAdministrator,
    GameMode Mode,
    ChallengeVisibility Visibility,
    string Title,
    string? Description,
    string Direction,
    string DefinitionJson,
    DateTimeOffset UpdatedAt);

public interface IChallengeBankStore
{
    Task<ChallengeTemplateWriteResult> CreateAsync(
        CreateChallengeTemplateCommand command,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChallengeTemplateView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<ChallengeTemplateListPage> ListPageAsync(
        ChallengeTemplateListQuery query,
        CancellationToken cancellationToken);
    Task<ChallengeTemplateView?> FindAsync(Guid challengeId, Guid actorId, bool isAdministrator, bool includeDeleted, CancellationToken cancellationToken);
    Task<ChallengeTemplateWriteResult> UpdateAsync(
        UpdateChallengeTemplateCommand command,
        CancellationToken cancellationToken);
    Task<ChallengeTemplateDeleteFailure?> SoftDeleteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeTemplateWriteResult> RestoreAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeTemplateWriteResult> UpdatePermissionsAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid[] managerIds,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeTemplateWriteResult> TransferOwnerAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public enum ChallengeTemplateDeleteFailure
{
    NotFound,
    InUse
}

public enum ChallengeTemplateValidationFailureCode
{
    InvalidMode,
    InvalidTitle,
    InvalidDirection,
    InvalidDefinition
}

public enum ChallengeTemplateDeleteFailureCode
{
    ChallengeInUse,
    ChallengeNotFound
}

public static class ChallengeTemplateValidation
{
    public static OperationResult<ChallengeTemplateValidationFailureCode> Validate(
        GameMode mode,
        string title,
        string direction,
        string definitionJson)
    {
        if (!Enum.IsDefined(mode))
            return OperationResult<ChallengeTemplateValidationFailureCode>.Failure(
                ChallengeTemplateValidationFailureCode.InvalidMode, "Mode is invalid.");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160)
            return OperationResult<ChallengeTemplateValidationFailureCode>.Failure(
                ChallengeTemplateValidationFailureCode.InvalidTitle,
                "Title is required and must be at most 160 characters.");
        if (string.IsNullOrWhiteSpace(direction) || direction.Length > 96)
            return OperationResult<ChallengeTemplateValidationFailureCode>.Failure(
                ChallengeTemplateValidationFailureCode.InvalidDirection,
                "Direction is required and must be at most 96 characters.");
        if (string.IsNullOrWhiteSpace(definitionJson))
            return OperationResult<ChallengeTemplateValidationFailureCode>.Failure(
                ChallengeTemplateValidationFailureCode.InvalidDefinition,
                "DefinitionJson is required.");
        return OperationResult<ChallengeTemplateValidationFailureCode>.Success();
    }
}

public sealed class CreateChallengeTemplate(
    IChallengeBankStore store,
    IChallengeConfigurationCatalog configurations,
    IExperimentalFeatureReader? experimentalFeatures = null)
{
    public async Task<ChallengeTemplateWriteResult> ExecuteAsync(
        CreateChallengeTemplateCommand command,
        CancellationToken ct = default)
    {
        var definitionJson = string.IsNullOrWhiteSpace(command.DefinitionJson)
            ? configurations.GetDefaultDefinitionJson(command.Mode)
            : command.DefinitionJson;
        var validation = ChallengeTemplateValidation.Validate(
            command.Mode,
            command.Title,
            command.Direction,
            definitionJson);
        if (!validation.Succeeded)
        {
            return new(
                ChallengeTemplateWriteState.InvalidRequest,
                Detail: validation.ErrorMessage);
        }
        var definitionErrors = configurations.ValidateDefinition(
            command.Mode,
            definitionJson);
        if (definitionErrors.Count > 0)
        {
            return new(
                ChallengeTemplateWriteState.InvalidDefinition,
                Detail: string.Join(" ", definitionErrors));
        }
        if (IsPatchVerification(command.Mode, definitionJson)
            && !(await IsPatchVerificationEnabledAsync(ct)))
        {
            return new(
                ChallengeTemplateWriteState.ExperimentalFeatureDisabled,
                Detail: "CTF PatchVerification is disabled in platform settings.");
        }
        return await store.CreateAsync(command with
        {
            Title = command.Title.Trim(),
            Description = command.Description?.Trim(),
            Direction = command.Direction.Trim(),
            DefinitionJson = definitionJson
        }, ct);
    }

    private Task<bool> IsPatchVerificationEnabledAsync(CancellationToken ct) =>
        experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
        ?? Task.FromResult(false);

    private static bool IsPatchVerification(GameMode mode, string definitionJson) =>
        mode == GameMode.Ctf
        && CtfInteractionDefinition.Parse(definitionJson) == CtfInteractionKind.PatchVerification;
}

public sealed class ListChallengeTemplates(
    IChallengeBankStore store,
    IExperimentalFeatureReader? experimentalFeatures = null)
{
    public async Task<ChallengeTemplateListPage> ExecutePageAsync(
        ChallengeTemplateListQuery query,
        CancellationToken ct = default)
    {
        var page = await store.ListPageAsync(query, ct);
        if (query.IsAdministrator || await IsEnabledAsync(ct))
            return page;

        var visible = page.Items
            .Where(item => item.InteractionKind != CtfInteractionKind.PatchVerification)
            .ToArray();
        return page with { Items = visible };
    }

    public async Task<IReadOnlyList<ChallengeTemplateView>> ExecuteAsync(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var items = await store.ListAsync(actorId, isAdministrator, includeDeleted, ct);
        if (isAdministrator || await IsEnabledAsync(ct))
            return items;
        return items.Where(item => item.InteractionKind != CtfInteractionKind.PatchVerification)
            .ToArray();
    }

    private Task<bool> IsEnabledAsync(CancellationToken ct) =>
        experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
        ?? Task.FromResult(false);
}

public sealed class GetChallengeTemplate(
    IChallengeBankStore store,
    IExperimentalFeatureReader? experimentalFeatures = null)
{
    public async Task<ChallengeTemplateView?> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var item = await store.FindAsync(challengeId, actorId, isAdministrator, includeDeleted, ct);
        if (item is null || isAdministrator
            || item.InteractionKind != CtfInteractionKind.PatchVerification
            || await IsEnabledAsync(ct))
            return item;
        return null;
    }

    private Task<bool> IsEnabledAsync(CancellationToken ct) =>
        experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
        ?? Task.FromResult(false);
}

public sealed class UpdateChallengeTemplate(
    IChallengeBankStore store,
    IChallengeConfigurationCatalog configurations)
{
    public async Task<ChallengeTemplateWriteResult> ExecuteAsync(
        UpdateChallengeTemplateCommand command,
        CancellationToken ct = default)
    {
        var definitionJson = string.IsNullOrWhiteSpace(command.DefinitionJson)
            ? configurations.GetDefaultDefinitionJson(command.Mode)
            : command.DefinitionJson;
        var validation = ChallengeTemplateValidation.Validate(
            command.Mode,
            command.Title,
            command.Direction,
            definitionJson);
        if (!validation.Succeeded)
        {
            return new(
                ChallengeTemplateWriteState.InvalidRequest,
                Detail: validation.ErrorMessage);
        }
        var definitionErrors = configurations.ValidateDefinition(
            command.Mode,
            definitionJson);
        if (definitionErrors.Count > 0)
        {
            return new(
                ChallengeTemplateWriteState.InvalidDefinition,
                Detail: string.Join(" ", definitionErrors));
        }
        return await store.UpdateAsync(command with
        {
            Title = command.Title.Trim(),
            Description = command.Description?.Trim(),
            Direction = command.Direction.Trim(),
            DefinitionJson = definitionJson
        }, ct);
    }

}

public sealed class DeleteChallengeTemplate(IChallengeBankStore store)
{
    public async Task<OperationResult<ChallengeTemplateDeleteFailureCode>> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var failure = await store.SoftDeleteAsync(
            challengeId,
            actorId,
            isAdministrator,
            now,
            ct);
        return failure switch
        {
            null => OperationResult<ChallengeTemplateDeleteFailureCode>.Success(),
            ChallengeTemplateDeleteFailure.InUse => OperationResult<ChallengeTemplateDeleteFailureCode>.Failure(
                ChallengeTemplateDeleteFailureCode.ChallengeInUse,
                "Challenge is still referenced by an active competition challenge or test Runtime."),
            _ => OperationResult<ChallengeTemplateDeleteFailureCode>.Failure(
                ChallengeTemplateDeleteFailureCode.ChallengeNotFound,
                "Challenge was not found or access was denied.")
        };
    }
}

public sealed class RestoreChallengeTemplate(IChallengeBankStore store)
{
    public Task<ChallengeTemplateWriteResult> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.RestoreAsync(challengeId, actorId, isAdministrator, now, ct);
}

public sealed class UpdateChallengeTemplatePermissions(IChallengeBankStore store)
{
    public Task<ChallengeTemplateWriteResult> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        IReadOnlyList<Guid> managerIds,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (managerIds.Any(id => id == Guid.Empty))
        {
            return Task.FromResult(new ChallengeTemplateWriteResult(
                ChallengeTemplateWriteState.InvalidRequest,
                Detail: "ManagerIds are invalid."));
        }
        return store.UpdatePermissionsAsync(
            challengeId,
            actorId,
            isAdministrator,
            managerIds.Distinct().ToArray(),
            now,
            ct);
    }
}

public sealed class TransferChallengeTemplateOwner(IChallengeBankStore store)
{
    public Task<ChallengeTemplateWriteResult> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (ownerId == Guid.Empty)
        {
            return Task.FromResult(new ChallengeTemplateWriteResult(
                ChallengeTemplateWriteState.InvalidRequest,
                Detail: "OwnerId is invalid."));
        }
        return store.TransferOwnerAsync(
            challengeId,
            actorId,
            isAdministrator,
            ownerId,
            now,
            ct);
    }
}
