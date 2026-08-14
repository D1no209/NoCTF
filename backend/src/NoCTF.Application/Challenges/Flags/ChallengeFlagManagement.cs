using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Common;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Challenges.Flags;

public readonly record struct ChallengeFlagScope(
    Guid? ChallengeId,
    Guid? CompetitionId,
    Guid? CompetitionChallengeId)
{
    public static ChallengeFlagScope Template(Guid challengeId) => new(challengeId, null, null);
    public static ChallengeFlagScope Competition(Guid competitionId, Guid competitionChallengeId) =>
        new(null, competitionId, competitionChallengeId);
}

public sealed record ChallengeFlagView(
    Guid Id,
    Guid? ChallengeId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    string Flag,
    ChallengeFlagMatchKind MatchKind,
    SpecificationKind? SpecificationKind,
    Guid? SpecificationId,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidUntil,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt);

public sealed record SaveChallengeFlagCommand(
    ChallengeFlagScope Scope,
    Guid? FlagId,
    bool IsCreate,
    Guid? TeamId,
    string Flag,
    SpecificationKind? SpecificationKind,
    Guid? SpecificationId,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidUntil,
    DateTimeOffset Now,
    ChallengeFlagMatchKind MatchKind = ChallengeFlagMatchKind.Exact);

public enum ChallengeFlagSaveFailure
{
    ScopeNotFound,
    FlagNotFound,
    ResourceIdConflict,
    SystemManagedFlag,
    DeliveryModeConflict
}

public enum ChallengeFlagMutationState
{
    Updated,
    NotFound,
    SystemManagedFlag
}

public enum ChallengeFlagFailureCode
{
    InvalidFlag,
    InvalidRegularExpression,
    RegularExpressionNotSupported,
    InvalidSpecification,
    InvalidValidityWindow,
    InvalidTemplateFlagScope,
    ResourceIdConflict,
    FlagNotFound,
    ManualFlagNotSupported,
    SystemManagedFlag,
    DeliveryModeConflict
}

public sealed record ChallengeFlagSaveResult(
    ChallengeFlagView? Flag,
    ChallengeFlagSaveFailure? Failure = null);

public interface IChallengeFlagStore
{
    Task<bool?> SupportsManualStaticFlagsAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<bool?> SupportsRegularExpressionAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChallengeFlagView>?> ListAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<ChallengeFlagView?> FindAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<ChallengeFlagSaveResult> SaveAsync(
        SaveChallengeFlagCommand command,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<ChallengeFlagMutationState> DeleteAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeFlagMutationState> RestoreAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ManageChallengeFlags(IChallengeFlagStore store)
{
    public Task<bool?> SupportsRegularExpressionAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        store.SupportsRegularExpressionAsync(scope, actorId, isAdministrator, ct);

    public Task<IReadOnlyList<ChallengeFlagView>?> ListAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.ListAsync(scope, actorId, isAdministrator, includeDeleted, ct);

    public Task<ChallengeFlagView?> GetAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.FindAsync(scope, flagId, actorId, isAdministrator, includeDeleted, ct);

    public async Task<OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>> SaveAsync(
        SaveChallengeFlagCommand command,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct = default)
    {
        if (!command.IsCreate && command.FlagId is Guid flagId)
        {
            var existing = await store.FindAsync(
                command.Scope,
                flagId,
                actorId,
                isAdministrator,
                includeDeleted: false,
                ct);
            if (existing is null)
                return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.FlagNotFound,
                    "Flag scope was not found, access was denied, or the Flag does not exist.");
            if (IsSystemManaged(existing))
                return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.SystemManagedFlag,
                    "System-managed Flags are read-only.");
        }
        var manualSupported = await store.SupportsManualStaticFlagsAsync(
            command.Scope,
            actorId,
            isAdministrator,
            ct);
        if (manualSupported is null)
            return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.FlagNotFound,
                "Flag scope was not found or access was denied.");
        if (!manualSupported.Value)
            return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.ManualFlagNotSupported,
                "Manual exact Flags are supported by static CTF and AWDP challenges only.");
        if (!Enum.IsDefined(command.MatchKind))
            return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.InvalidFlag,
                "Flag match kind is invalid.");
        var bytes = Encoding.UTF8.GetBytes(command.Flag);
        if (bytes.Length is < 1 or > 4096 || bytes.Contains((byte)0))
            return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.InvalidFlag,
                "Flag must be 1..4096 UTF-8 bytes and cannot contain NUL.");
        if (command.MatchKind == ChallengeFlagMatchKind.RegularExpression)
        {
            if (!ChallengeFlagMatcher.IsValidRegularExpression(command.Flag))
                return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.InvalidRegularExpression,
                    "Flag regular expression is invalid or uses unsupported constructs.");
            var supported = await store.SupportsRegularExpressionAsync(
                command.Scope,
                actorId,
                isAdministrator,
                ct);
            if (supported is null)
                return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.FlagNotFound,
                    "Flag scope was not found or access was denied.");
            if (!supported.Value)
                return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.RegularExpressionNotSupported,
                    "Regular-expression flags are only supported by CTF challenges without dynamic per-team flags.");
        }
        if ((command.SpecificationKind is null) != (command.SpecificationId is null))
            return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.InvalidSpecification,
                "SpecificationKind and SpecificationId must both be present or absent.");
        if (command.ValidStart is not null &&
            command.ValidUntil is not null &&
            command.ValidStart >= command.ValidUntil)
            return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.InvalidValidityWindow,
                "ValidStart must be earlier than ValidUntil.");
        if (command.Scope.ChallengeId is not null && teamOrWindowPresent(command))
            return OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.InvalidTemplateFlagScope,
                "Template flags cannot be team-scoped or time-windowed.");

        var result = await store.SaveAsync(command, actorId, isAdministrator, ct);
        return result.Failure switch
        {
            ChallengeFlagSaveFailure.ResourceIdConflict =>
                OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.ResourceIdConflict,
                    "The requested flag ID is already in use."),
            ChallengeFlagSaveFailure.ScopeNotFound or ChallengeFlagSaveFailure.FlagNotFound =>
                OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.FlagNotFound,
                    "Flag scope was not found, access was denied, or the flag does not exist."),
            ChallengeFlagSaveFailure.SystemManagedFlag =>
                OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.SystemManagedFlag,
                    "System-managed Flags are read-only."),
            ChallengeFlagSaveFailure.DeliveryModeConflict =>
                OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.DeliveryModeConflict,
                    "Static Flags cannot be mixed with RandomOnePerTeam attachment variants."),
            _ => OperationResult<ChallengeFlagView, ChallengeFlagFailureCode>.Success(result.Flag!)
        };
    }

    public async Task<OperationResult<ChallengeFlagFailureCode>> DeleteAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        (await store.DeleteAsync(scope, flagId, actorId, isAdministrator, now, ct)) switch
        {
            ChallengeFlagMutationState.Updated =>
                OperationResult<ChallengeFlagFailureCode>.Success(),
            ChallengeFlagMutationState.SystemManagedFlag =>
                OperationResult<ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.SystemManagedFlag,
                    "System-managed Flags are read-only."),
            _ => OperationResult<ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.FlagNotFound,
                "Flag was not found or access was denied.")
        };

    public async Task<OperationResult<ChallengeFlagFailureCode>> RestoreAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        (await store.RestoreAsync(scope, flagId, actorId, isAdministrator, now, ct)) switch
        {
            ChallengeFlagMutationState.Updated =>
                OperationResult<ChallengeFlagFailureCode>.Success(),
            ChallengeFlagMutationState.SystemManagedFlag =>
                OperationResult<ChallengeFlagFailureCode>.Failure(
                    ChallengeFlagFailureCode.SystemManagedFlag,
                    "System-managed Flags are read-only."),
            _ => OperationResult<ChallengeFlagFailureCode>.Failure(
                ChallengeFlagFailureCode.FlagNotFound,
                "Deleted flag was not found or access was denied.")
        };

    private static bool teamOrWindowPresent(SaveChallengeFlagCommand command) =>
        command.TeamId is not null || command.ValidStart is not null || command.ValidUntil is not null;

    private static bool IsSystemManaged(ChallengeFlagView flag) =>
        flag.TeamId is not null
        || flag.SpecificationKind is not null
        || flag.SpecificationId is not null
        || flag.ValidStart is not null
        || flag.ValidUntil is not null;

    public static byte[] Hash(string flag) => SHA256.HashData(Encoding.UTF8.GetBytes(flag));
}
