using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public enum RuntimeAction
{
    Start,
    Stop,
    Reset,
    Extend
}

public sealed record RuntimeInstanceView(
    Guid Id,
    Guid? CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? ChallengeId,
    Guid? TeamId,
    RuntimePurpose Purpose,
    RuntimeKind RuntimeKind,
    RuntimeProvider Provider,
    RuntimeState State,
    RuntimeFailureCode? FailureCode,
    IReadOnlyList<string> Urls,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RunningAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StoppedAt,
    string? RunnerId = null,
    IReadOnlyList<RuntimePublishedPortView>? PublishedPorts = null,
    DateTimeOffset? StateChangedAt = null,
    Guid? SourceTeamId = null,
    string? SourceTeamName = null,
    GameMode? Mode = null,
    IReadOnlyList<NoCTF.Application.Runtime.Provisioning.RuntimeUrlBinding>? AccessBindings = null);

public sealed record RuntimePublishedPortView(
    string? ServiceName,
    int ContainerPort,
    int HostPort);

public sealed record RuntimeMutationCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid UserId,
    RuntimeAction Action,
    TimeSpan? Extension,
    DateTimeOffset Now);

public enum RuntimeMutationFailure
{
    NotFound,
    Unsupported,
    InvalidState,
    CapacityExceeded,
    Conflict,
    ConfigurationInvalid,
    NotStuck,
    InvalidReason
}

public enum RuntimeMutationFailureCode
{
    InvalidExtension,
    RuntimeNotFound,
    RuntimeActionUnsupported,
    RuntimeStateConflict,
    RuntimeCapacityExceeded,
    RuntimeConfigurationInvalid,
    RuntimeConflict
}

public sealed record RuntimeMutationResult(
    RuntimeInstanceView? Runtime,
    RuntimeMutationFailure? Failure = null);

public interface IRuntimeInstanceStore
{
    Task<RuntimeInstanceView?> FindPlayerRuntimeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);
    Task<RuntimeMutationResult> MutatePlayerRuntimeAsync(
        RuntimeMutationCommand command,
        CancellationToken cancellationToken);
}

public sealed record RuntimeTargetView(Guid TeamId, string TeamName, IReadOnlyList<string> Urls);

public interface IRuntimeTargetReader
{
    Task<IReadOnlyList<RuntimeTargetView>?> ListAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ListRuntimeTargets(IRuntimeTargetReader reader)
{
    public Task<IReadOnlyList<RuntimeTargetView>?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        reader.ListAsync(competitionId, competitionChallengeId, userId, now, ct);
}

public sealed class GetPlayerRuntime(IRuntimeInstanceStore store)
{
    public Task<RuntimeInstanceView?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct = default) =>
        store.FindPlayerRuntimeAsync(competitionId, competitionChallengeId, userId, ct);
}

public sealed class MutatePlayerRuntime(IRuntimeInstanceStore store)
{
    public async Task<OperationResult<RuntimeInstanceView, RuntimeMutationFailureCode>> ExecuteAsync(
        RuntimeMutationCommand command,
        CancellationToken ct = default)
    {
        if (command.Action == RuntimeAction.Extend &&
            (command.Extension is null || command.Extension <= TimeSpan.Zero || command.Extension > TimeSpan.FromHours(24)))
        {
            return OperationResult<RuntimeInstanceView, RuntimeMutationFailureCode>.Failure(
                RuntimeMutationFailureCode.InvalidExtension,
                "Extension must be greater than zero and no more than 24 hours.");
        }

        var result = await store.MutatePlayerRuntimeAsync(command, ct);
        if (result.Runtime is not null)
            return OperationResult<RuntimeInstanceView, RuntimeMutationFailureCode>.Success(result.Runtime);
        var failure = result.Failure ?? RuntimeMutationFailure.Conflict;
        var failureCode = failure switch
        {
            RuntimeMutationFailure.NotFound => RuntimeMutationFailureCode.RuntimeNotFound,
            RuntimeMutationFailure.Unsupported => RuntimeMutationFailureCode.RuntimeActionUnsupported,
            RuntimeMutationFailure.InvalidState => RuntimeMutationFailureCode.RuntimeStateConflict,
            RuntimeMutationFailure.CapacityExceeded => RuntimeMutationFailureCode.RuntimeCapacityExceeded,
            RuntimeMutationFailure.ConfigurationInvalid => RuntimeMutationFailureCode.RuntimeConfigurationInvalid,
            _ => RuntimeMutationFailureCode.RuntimeConflict
        };
        var detail = failure switch
        {
            RuntimeMutationFailure.NotFound =>
                "No runtime exists for this team and challenge.",
            RuntimeMutationFailure.Unsupported =>
                "This challenge does not support the requested runtime operation.",
            RuntimeMutationFailure.InvalidState =>
                $"The runtime is not in a state that permits the requested {command.Action.ToString().ToLowerInvariant()} operation. Refresh the runtime status before retrying.",
            RuntimeMutationFailure.CapacityExceeded =>
                "No runner currently has enough capacity to start this runtime. Stop an unused runtime or try again later.",
            RuntimeMutationFailure.ConfigurationInvalid =>
                "The challenge runtime configuration is invalid. Ask a competition administrator to review it.",
            _ =>
                "The runtime state changed while this request was being processed. Refresh the runtime status before retrying."
        };
        return OperationResult<RuntimeInstanceView, RuntimeMutationFailureCode>.Failure(
            failureCode,
            detail);
    }
}
