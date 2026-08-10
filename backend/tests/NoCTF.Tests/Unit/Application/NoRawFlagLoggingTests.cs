using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public class NoRawFlagLoggingTests
{
    [Test]
    public async Task Flag_input_string_representation_redacts_plaintext()
    {
        const string rawFlag = "flag{do-not-leak}";
        var value = new FlagGameplayFactReceived(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            GameplayFactKind.FlagAttempt,
            rawFlag,
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rawFlag)),
            DateTimeOffset.UtcNow);

        await Assert.That(value.ToString()).DoesNotContain("do-not-leak");
    }
}
