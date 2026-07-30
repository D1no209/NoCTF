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
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    int ActiveCompetitionReferenceCount);

public sealed record CreateChallengeTemplateCommand(
    Guid? Id,
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
    int ExpectedRevision,
    DateTimeOffset UpdatedAt);

public interface IChallengeBankStore
{
    Task<ChallengeTemplateView> CreateAsync(CreateChallengeTemplateCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChallengeTemplateView>> ListAsync(Guid actorId, bool isAdministrator, CancellationToken cancellationToken);
    Task<ChallengeTemplateView?> FindAsync(Guid challengeId, Guid actorId, bool isAdministrator, bool includeDeleted, CancellationToken cancellationToken);
    Task<ChallengeTemplateView?> UpdateAsync(UpdateChallengeTemplateCommand command, CancellationToken cancellationToken);
    Task<bool> SoftDeleteAsync(Guid challengeId, Guid actorId, bool isAdministrator, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RestoreAsync(Guid challengeId, Guid actorId, bool isAdministrator, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ChallengeTemplateView?> UpdatePermissionsAsync(Guid challengeId, Guid actorId, bool isAdministrator, Guid[] managerIds, int expectedRevision, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ChallengeTemplateView?> TransferOwnerAsync(Guid challengeId, Guid actorId, bool isAdministrator, Guid ownerId, int expectedRevision, DateTimeOffset now, CancellationToken cancellationToken);
}

public static class ChallengeTemplateValidation
{
    public static OperationResult Validate(
        GameMode mode,
        string title,
        string direction,
        string definitionJson,
        int expectedRevision = 0)
    {
        if (!Enum.IsDefined(mode))
            return OperationResult.Failure("invalid_mode", "Mode is invalid.");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160)
            return OperationResult.Failure("invalid_title", "Title is required and must be at most 160 characters.");
        if (string.IsNullOrWhiteSpace(direction) || direction.Length > 96)
            return OperationResult.Failure("invalid_direction", "Direction is required and must be at most 96 characters.");
        if (string.IsNullOrWhiteSpace(definitionJson))
            return OperationResult.Failure("invalid_definition", "DefinitionJson is required.");
        if (expectedRevision < 0)
            return OperationResult.Failure("invalid_revision", "ExpectedRevision cannot be negative.");
        return OperationResult.Success();
    }
}

public sealed class CreateChallengeTemplate(
    IChallengeBankStore store,
    IChallengeConfigurationCatalog configurations)
{
    public async Task<OperationResult<ChallengeTemplateView>> ExecuteAsync(
        CreateChallengeTemplateCommand command,
        CancellationToken ct = default)
    {
        var validation = ChallengeTemplateValidation.Validate(
            command.Mode,
            command.Title,
            command.Direction,
            command.DefinitionJson);
        if (!validation.Succeeded)
            return OperationResult<ChallengeTemplateView>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        var definitionErrors = configurations.ValidateDefinition(
            command.Mode,
            command.DefinitionJson);
        if (definitionErrors.Count > 0)
            return OperationResult<ChallengeTemplateView>.Failure(
                "invalid_definition",
                string.Join(" ", definitionErrors));
        if (command.Id == Guid.Empty)
            return OperationResult<ChallengeTemplateView>.Failure(
                "invalid_challenge_id",
                "Id must be omitted or contain a non-empty UUID.");
        var result = await store.CreateAsync(command with
        {
            Title = command.Title.Trim(),
            Description = command.Description?.Trim(),
            Direction = command.Direction.Trim()
        }, ct);
        return OperationResult<ChallengeTemplateView>.Success(result);
    }
}

public sealed class ListChallengeTemplates(IChallengeBankStore store)
{
    public Task<IReadOnlyList<ChallengeTemplateView>> ExecuteAsync(
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        store.ListAsync(actorId, isAdministrator, ct);
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
    public async Task<OperationResult<ChallengeTemplateView>> ExecuteAsync(
        UpdateChallengeTemplateCommand command,
        CancellationToken ct = default)
    {
        var validation = ChallengeTemplateValidation.Validate(
            command.Mode,
            command.Title,
            command.Direction,
            command.DefinitionJson,
            command.ExpectedRevision);
        if (!validation.Succeeded)
            return OperationResult<ChallengeTemplateView>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        var definitionErrors = configurations.ValidateDefinition(
            command.Mode,
            command.DefinitionJson);
        if (definitionErrors.Count > 0)
            return OperationResult<ChallengeTemplateView>.Failure(
                "invalid_definition",
                string.Join(" ", definitionErrors));
        var result = await store.UpdateAsync(command with
        {
            Title = command.Title.Trim(),
            Description = command.Description?.Trim(),
            Direction = command.Direction.Trim()
        }, ct);
        return result is null
            ? OperationResult<ChallengeTemplateView>.Failure("challenge_not_found_or_revision_conflict", "Challenge was not found, access was denied, or its revision changed.")
            : OperationResult<ChallengeTemplateView>.Success(result);
    }
}

public sealed class DeleteChallengeTemplate(IChallengeBankStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        bool restore,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var changed = restore
            ? await store.RestoreAsync(challengeId, actorId, isAdministrator, now, ct)
            : await store.SoftDeleteAsync(challengeId, actorId, isAdministrator, now, ct);
        return changed
            ? OperationResult.Success()
            : OperationResult.Failure("challenge_not_found", "Challenge was not found or access was denied.");
    }
}

public sealed class UpdateChallengeTemplatePermissions(IChallengeBankStore store)
{
    public async Task<OperationResult<ChallengeTemplateView>> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        IReadOnlyList<Guid> managerIds,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (expectedRevision < 0 || managerIds.Any(id => id == Guid.Empty))
            return OperationResult<ChallengeTemplateView>.Failure(
                "invalid_permissions",
                "ManagerIds and ExpectedRevision are invalid.");
        var result = await store.UpdatePermissionsAsync(
            challengeId,
            actorId,
            isAdministrator,
            managerIds.Distinct().ToArray(),
            expectedRevision,
            now,
            ct);
        return result is null
            ? OperationResult<ChallengeTemplateView>.Failure(
                "challenge_not_found_or_revision_conflict",
                "Challenge was not found, access was denied, or its revision changed.")
            : OperationResult<ChallengeTemplateView>.Success(result);
    }
}

public sealed class TransferChallengeTemplateOwner(IChallengeBankStore store)
{
    public async Task<OperationResult<ChallengeTemplateView>> ExecuteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (ownerId == Guid.Empty || expectedRevision < 0)
            return OperationResult<ChallengeTemplateView>.Failure(
                "invalid_owner_transfer",
                "OwnerId and ExpectedRevision are invalid.");
        var result = await store.TransferOwnerAsync(
            challengeId,
            actorId,
            isAdministrator,
            ownerId,
            expectedRevision,
            now,
            ct);
        return result is null
            ? OperationResult<ChallengeTemplateView>.Failure(
                "challenge_not_found_or_revision_conflict",
                "Challenge was not found, access was denied, or its revision changed.")
            : OperationResult<ChallengeTemplateView>.Success(result);
    }
}
