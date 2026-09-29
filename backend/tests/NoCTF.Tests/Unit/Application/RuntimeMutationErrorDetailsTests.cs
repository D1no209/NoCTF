using NSubstitute;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit.Application;

public sealed class RuntimeMutationErrorDetailsTests
{
    [Test]
    [Arguments(
        RuntimeMutationFailure.InvalidState,
        RuntimeMutationFailureCode.RuntimeStateConflict,
        "not in a state")]
    [Arguments(
        RuntimeMutationFailure.ExtensionTooEarly,
        RuntimeMutationFailureCode.RuntimeExtensionTooEarly,
        "final ten minutes")]
    [Arguments(
        RuntimeMutationFailure.Conflict,
        RuntimeMutationFailureCode.RuntimeConflict,
        "state changed")]
    [Arguments(
        RuntimeMutationFailure.CapacityExceeded,
        RuntimeMutationFailureCode.RuntimeCapacityExceeded,
        "enough capacity")]
    [Arguments(
        RuntimeMutationFailure.ConfigurationInvalid,
        RuntimeMutationFailureCode.RuntimeConfigurationInvalid,
        "configuration is invalid")]
    [Arguments(
        RuntimeMutationFailure.Unsupported,
        RuntimeMutationFailureCode.RuntimeActionUnsupported,
        "does not support")]
    public async Task ExecuteAsync_StoreFailure_ReturnsActionableDetail(
        RuntimeMutationFailure storeFailure,
        RuntimeMutationFailureCode expectedCode,
        string expectedDetail)
    {
        var store = Substitute.For<IRuntimeInstanceStore>();
        store.MutatePlayerRuntimeAsync(
                Arg.Any<RuntimeMutationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new RuntimeMutationResult(null, storeFailure));

        var result = await new MutatePlayerRuntime(store).ExecuteAsync(new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            RuntimeAction.Start,
            null,
            DateTimeOffset.UtcNow));

        await Assert.That(result.FailureCode).IsEqualTo(expectedCode);
        await Assert.That(result.ErrorMessage).Contains(expectedDetail);
        await Assert.That(result.ErrorMessage).DoesNotContain("Runtime action was rejected");
    }
}
