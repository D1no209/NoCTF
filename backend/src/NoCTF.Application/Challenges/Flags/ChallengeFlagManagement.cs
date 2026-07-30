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
    SpecificationKind? SpecificationKind,
    Guid? SpecificationId,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SaveChallengeFlagCommand(
    ChallengeFlagScope Scope,
    Guid? FlagId,
    Guid? TeamId,
    string Flag,
    SpecificationKind? SpecificationKind,
    Guid? SpecificationId,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidUntil,
    DateTimeOffset Now,
    Guid? RequestedId = null);

public interface IChallengeFlagStore
{
    Task<IReadOnlyList<ChallengeFlagView>?> ListAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken cancellationToken,
        bool includeDeleted = false);
    Task<ChallengeFlagView?> FindAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken cancellationToken,
        bool includeDeleted = false);
    Task<ChallengeFlagView?> SaveAsync(
        SaveChallengeFlagCommand command,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> RestoreAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
}

public sealed class ManageChallengeFlags(IChallengeFlagStore store)
{
    public Task<IReadOnlyList<ChallengeFlagView>?> ListAsync(
        ChallengeFlagScope scope,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct = default,
        bool includeDeleted = false) =>
        store.ListAsync(scope, actorId, isAdministrator, ct, includeDeleted);

    public Task<ChallengeFlagView?> GetAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct = default,
        bool includeDeleted = false) =>
        store.FindAsync(scope, flagId, actorId, isAdministrator, ct, includeDeleted);

    public async Task<OperationResult<ChallengeFlagView>> SaveAsync(
        SaveChallengeFlagCommand command,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(command.Flag);
        if (command.RequestedId == Guid.Empty)
            return OperationResult<ChallengeFlagView>.Failure(
                "invalid_flag_id",
                "Id must be omitted or contain a non-empty UUID.");
        if (bytes.Length is < 1 or > 4096 || bytes.Contains((byte)0))
            return OperationResult<ChallengeFlagView>.Failure(
                "invalid_flag",
                "Flag must be 1..4096 UTF-8 bytes and cannot contain NUL.");
        if ((command.SpecificationKind is null) != (command.SpecificationId is null))
            return OperationResult<ChallengeFlagView>.Failure(
                "invalid_specification",
                "SpecificationKind and SpecificationId must both be present or absent.");
        if (command.ValidStart is not null &&
            command.ValidUntil is not null &&
            command.ValidStart >= command.ValidUntil)
            return OperationResult<ChallengeFlagView>.Failure(
                "invalid_validity_window",
                "ValidStart must be earlier than ValidUntil.");
        if (command.Scope.ChallengeId is not null && teamOrWindowPresent(command))
            return OperationResult<ChallengeFlagView>.Failure(
                "invalid_template_flag_scope",
                "Template flags cannot be team-scoped or time-windowed.");

        var result = await store.SaveAsync(command, actorId, isAdministrator, ct);
        return result is null
            ? OperationResult<ChallengeFlagView>.Failure(
                "flag_not_found",
                "Flag scope was not found, access was denied, or the flag does not exist.")
            : OperationResult<ChallengeFlagView>.Success(result);
    }

    public async Task<OperationResult> DeleteAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.DeleteAsync(scope, flagId, actorId, isAdministrator, now, ct)
            ? OperationResult.Success()
            : OperationResult.Failure("flag_not_found", "Flag was not found or access was denied.");

    public async Task<OperationResult> RestoreAsync(
        ChallengeFlagScope scope,
        Guid flagId,
        Guid? actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        await store.RestoreAsync(scope, flagId, actorId, isAdministrator, ct)
            ? OperationResult.Success()
            : OperationResult.Failure(
                "flag_not_found",
                "Deleted flag was not found or access was denied.");

    private static bool teamOrWindowPresent(SaveChallengeFlagCommand command) =>
        command.TeamId is not null || command.ValidStart is not null || command.ValidUntil is not null;

    public static byte[] Hash(string flag) => SHA256.HashData(Encoding.UTF8.GetBytes(flag));
}
