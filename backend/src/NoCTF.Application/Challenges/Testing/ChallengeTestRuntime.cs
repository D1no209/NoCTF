using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Challenges.Testing;

public sealed record ChallengeTestRuntimeView(
    Guid Id,
    Guid ChallengeId,
    RuntimeKind RuntimeKind,
    RuntimeProvider Provider,
    RuntimeState State,
    RuntimeFailureCode? FailureCode,
    RuntimeTestFlagDelivery FlagDelivery,
    RuntimeTestFlagState FlagState,
    string? TestFlag,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RunningAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StoppedAt,
    RuntimeAccessMode AccessMode = RuntimeAccessMode.Direct,
    IReadOnlyList<RuntimeAccessEndpointView>? AccessEndpoints = null);

public sealed record ChallengeTestRuntimeCommand(
    Guid ChallengeId,
    Guid ActorUserId,
    bool IsAdministrator,
    RuntimeAction Action,
    TimeSpan? Extension,
    DateTimeOffset Now);

public sealed record InjectChallengeTestFlag(
    Guid RuntimeInstanceId,
    Guid ChallengeId,
    Guid ChallengeFlagId,
    string RunnerId,
    int FailedAttempts = 0) : IRunnerNodeMessage;

public sealed record ChallengeTestRuntimeResult(
    ChallengeTestRuntimeView? Runtime,
    RuntimeMutationFailure? Failure = null);

public interface IChallengeTestRuntimeStore
{
    Task<ChallengeTestRuntimeView?> FindAsync(
        Guid challengeId,
        Guid actorUserId,
        bool isAdministrator,
        CancellationToken cancellationToken);

    Task<ChallengeTestRuntimeResult> MutateAsync(
        ChallengeTestRuntimeCommand command,
        CancellationToken cancellationToken);
}

public sealed class GetChallengeTestRuntime(IChallengeTestRuntimeStore store)
{
    public Task<ChallengeTestRuntimeView?> ExecuteAsync(
        Guid challengeId,
        Guid actorUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default) =>
        store.FindAsync(challengeId, actorUserId, isAdministrator, cancellationToken);
}

public sealed class MutateChallengeTestRuntime(IChallengeTestRuntimeStore store)
{
    public Task<ChallengeTestRuntimeResult> ExecuteAsync(
        ChallengeTestRuntimeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Action == RuntimeAction.Extend
            && (command.Extension is null
                || command.Extension <= TimeSpan.Zero
                || command.Extension > TimeSpan.FromHours(24)))
        {
            return Task.FromResult(new ChallengeTestRuntimeResult(
                null,
                RuntimeMutationFailure.InvalidState));
        }

        return store.MutateAsync(command, cancellationToken);
    }
}
