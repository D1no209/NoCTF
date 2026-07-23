using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Application;

public class NoRawFlagLoggingTests
{
    [Test]
    public async Task Flag_input_string_representation_redacts_plaintext()
    {
        const string rawFlag = "flag{do-not-leak}";
        var value = new FlagSubmissionReceived(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SubmissionKind.Flag,
            rawFlag,
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rawFlag)),
            DateTimeOffset.UtcNow);

        await Assert.That(value.ToString()).DoesNotContain("do-not-leak");
    }
}
