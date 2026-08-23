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
    DateTimeOffset UpdatedAt);

public enum ChallengeTemplateWriteState
{
    Succeeded,
    InvalidRequest,
    InvalidDefinition,
    ResourceIdConflict,
    NotFoundOrForbidden,
    ActiveCompetitionModeConflict,
    OwnerIncludedInManagerSet,
    UserNotFound,
    RoleNotEligible
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
    IChallengeConfigurationCatalog configurations)
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
        return await store.CreateAsync(command with
        {
            Title = command.Title.Trim(),
            Description = command.Description?.Trim(),
            Direction = command.Direction.Trim(),
            DefinitionJson = definitionJson
        }, ct);
    }
}

public sealed class ListChallengeTemplates(IChallengeBankStore store)
{
    public Task<IReadOnlyList<ChallengeTemplateView>> ExecuteAsync(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.ListAsync(actorId, isAdministrator, includeDeleted, ct);
}

public sealed class GetChallengeTemplate(IChallengeBankStore store)
{
    public Task<ChallengeTemplateView?> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.FindAsync(challengeId, actorId, isAdministrator, includeDeleted, ct);
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
                "Challenge is still referenced by an active competition challenge."),
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
