using NoCTF.API.Endpoints.Submissions;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.API;

public class SubmissionMapperTests
{
    [Test]
    [Arguments(null, SubmissionStatusState.Unprocessed)]
    [Arguments(ScoringResult.Correct, SubmissionStatusState.Success)]
    [Arguments(ScoringResult.Wrong, SubmissionStatusState.Failure)]
    [Arguments(ScoringResult.Duplicate, SubmissionStatusState.Failure)]
    [Arguments(ScoringResult.AttemptsExhausted, SubmissionStatusState.Failure)]
    [Arguments(ScoringResult.PlatformFailed, SubmissionStatusState.Failure)]
    [Arguments(ScoringResult.Rejected, SubmissionStatusState.Failure)]
    public async Task StatusMapping_UsesCurrentScoringEventResult(
        ScoringResult? result,
        SubmissionStatusState expected)
    {
        var view = new SubmissionStatusView(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            SubmissionKind.Flag, result, DateTimeOffset.UtcNow,
            result is null ? null : DateTimeOffset.UtcNow,
            result == ScoringResult.Wrong ? ScoringFailureCode.FlagNotSupported : null,
            result is null ? null : "test-evaluator");

        var response = SubmissionMapper.ToStatusResponse(view);

        await Assert.That(response.State).IsEqualTo(expected);
        await Assert.That(response.SubmissionId).IsEqualTo(view.SubmissionId);
        await Assert.That(response.Kind).IsEqualTo(view.Kind);
        await Assert.That(response.FailureCode).IsEqualTo(view.FailureCode);
    }
}
