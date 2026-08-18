using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public sealed class RequestAwdpDefenseTargetTests
{
    [Test]
    public async Task Created_target_is_returned_with_runtime_identity()
    {
        var runtimeId = Guid.CreateVersion7();
        var useCase = new RequestAwdpDefenseTarget(new StubStore(new(
            AwdpDefenseTargetRequestState.Created,
            runtimeId,
            RuntimeState.Queued)));

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.RuntimeInstanceId).IsEqualTo(runtimeId);
        await Assert.That(result.Value.State).IsEqualTo(RuntimeState.Queued);
    }

    [Test]
    [Arguments(
        AwdpDefenseTargetRequestState.ActiveTargetExists,
        AwdpDefenseTargetRequestFailureCode.ActiveDefenseTargetExists)]
    [Arguments(
        AwdpDefenseTargetRequestState.BreakRequired,
        AwdpDefenseTargetRequestFailureCode.BreakRequired)]
    [Arguments(
        AwdpDefenseTargetRequestState.AttemptsExhausted,
        AwdpDefenseTargetRequestFailureCode.FixAttemptsExhausted)]
    [Arguments(
        AwdpDefenseTargetRequestState.InvalidConfiguration,
        AwdpDefenseTargetRequestFailureCode.InvalidRuntimeConfiguration)]
    [Arguments(
        AwdpDefenseTargetRequestState.ConcurrencyConflict,
        AwdpDefenseTargetRequestFailureCode.DefenseTargetConcurrency)]
    [Arguments(
        AwdpDefenseTargetRequestState.ScopeNotFound,
        AwdpDefenseTargetRequestFailureCode.DefenseNotAvailable)]
    public async Task Store_states_map_to_stable_failure_codes(
        AwdpDefenseTargetRequestState state,
        AwdpDefenseTargetRequestFailureCode expected)
    {
        var useCase = new RequestAwdpDefenseTarget(new StubStore(new(state)));

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(expected);
    }

    private sealed class StubStore(AwdpDefenseTargetRequestResult result)
        : IAwdpDefenseTargetStore
    {
        public Task<AwdpDefenseTargetRequestResult> TryCreateAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
