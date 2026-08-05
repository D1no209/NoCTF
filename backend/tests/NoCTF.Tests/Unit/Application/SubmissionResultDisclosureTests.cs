using NoCTF.Application.Submissions.Status;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Application;

public sealed class SubmissionResultDisclosureTests
{
    [Test]
    [Arguments(ScoringFailureCode.ForeignTeamFlagDetected)]
    [Arguments(ScoringFailureCode.AmbiguousFlagMatch)]
    public async Task Protected_anti_cheat_results_look_like_an_ordinary_wrong_flag(
        ScoringFailureCode failureCode)
    {
        await Assert.That(SubmissionResultDisclosure.PlayerResult(
                ScoringResult.Rejected,
                failureCode))
            .IsEqualTo(ScoringResult.Wrong);
        await Assert.That(SubmissionResultDisclosure.PlayerFailureCode(failureCode))
            .IsNull();
    }
}
