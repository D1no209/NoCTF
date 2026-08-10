using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public sealed class GameplayFactResultDisclosureTests
{
    [Test]
    [Arguments(GameplayFactFailureCode.ForeignTeamFlagDetected)]
    [Arguments(GameplayFactFailureCode.AmbiguousFlagMatch)]
    public async Task Protected_anti_cheat_results_look_like_an_ordinary_wrong_flag(
        GameplayFactFailureCode failureCode)
    {
        await Assert.That(GameplayFactResultDisclosure.PlayerResult(
                GameplayFactResult.Rejected,
                failureCode))
            .IsEqualTo(GameplayFactResult.Wrong);
        await Assert.That(GameplayFactResultDisclosure.PlayerFailureCode(failureCode))
            .IsNull();
    }
}
