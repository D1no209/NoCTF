using NoCTF.Application.Common;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.GameplayFacts.Awdp;

public enum AwdpDefenseTargetRequestState
{
    Created,
    ScopeNotFound,
    ActiveTargetExists,
    BreakRequired,
    AttemptsExhausted,
    InvalidConfiguration,
    ConcurrencyConflict
}

public enum AwdpDefenseTargetRequestFailureCode
{
    DefenseNotAvailable,
    ActiveDefenseTargetExists,
    BreakRequired,
    FixAttemptsExhausted,
    InvalidRuntimeConfiguration,
    DefenseTargetConcurrency
}

public sealed record AwdpDefenseTargetRequestResult(
    AwdpDefenseTargetRequestState State,
    Guid? RuntimeInstanceId = null,
    RuntimeState? RuntimeState = null);

public interface IAwdpDefenseTargetStore
{
    Task<AwdpDefenseTargetRequestResult> TryCreateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record RequestedAwdpDefenseTarget(
    Guid RuntimeInstanceId,
    RuntimeState State);

public sealed class RequestAwdpDefenseTarget(IAwdpDefenseTargetStore store)
{
    public async Task<OperationResult<RequestedAwdpDefenseTarget,
        AwdpDefenseTargetRequestFailureCode>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var result = await store.TryCreateAsync(
            competitionId,
            competitionChallengeId,
            userId,
            now,
            cancellationToken);
        return result.State switch
        {
            AwdpDefenseTargetRequestState.Created =>
                OperationResult<RequestedAwdpDefenseTarget,
                    AwdpDefenseTargetRequestFailureCode>.Success(new(
                        result.RuntimeInstanceId!.Value,
                        result.RuntimeState!.Value)),
            AwdpDefenseTargetRequestState.ActiveTargetExists =>
                Failure(
                    AwdpDefenseTargetRequestFailureCode.ActiveDefenseTargetExists,
                    "An active one-shot defense target already exists."),
            AwdpDefenseTargetRequestState.BreakRequired =>
                Failure(
                    AwdpDefenseTargetRequestFailureCode.BreakRequired,
                    "A successful Break is required before requesting a defense target."),
            AwdpDefenseTargetRequestState.AttemptsExhausted =>
                Failure(
                    AwdpDefenseTargetRequestFailureCode.FixAttemptsExhausted,
                    "The maximum number of Fix attempts has been reached."),
            AwdpDefenseTargetRequestState.InvalidConfiguration =>
                Failure(
                    AwdpDefenseTargetRequestFailureCode.InvalidRuntimeConfiguration,
                    "The AWDP defense target configuration is invalid."),
            AwdpDefenseTargetRequestState.ConcurrencyConflict =>
                Failure(
                    AwdpDefenseTargetRequestFailureCode.DefenseTargetConcurrency,
                    "The defense target state changed concurrently."),
            _ => Failure(
                AwdpDefenseTargetRequestFailureCode.DefenseNotAvailable,
                "A one-shot defense target is not available for this team and challenge.")
        };
    }

    private static OperationResult<RequestedAwdpDefenseTarget,
        AwdpDefenseTargetRequestFailureCode> Failure(
        AwdpDefenseTargetRequestFailureCode code,
        string message) =>
        OperationResult<RequestedAwdpDefenseTarget,
            AwdpDefenseTargetRequestFailureCode>.Failure(code, message);
}
