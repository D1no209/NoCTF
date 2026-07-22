using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Application;

public class NoRawFlagLoggingTests
{
    [Test]
    public async Task Flag_input_string_representation_redacts_plaintext()
    {
        var value = new FlagSubmissionReceived
        {
            SubmissionId = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FlagFingerprint = FlagFingerprint.Create("flag{do-not-leak}"),
            IdempotencyKey = "test-1",
            IpAddress = "127.0.0.1",
            ReceivedAt = DateTimeOffset.UtcNow
        };

        await Assert.That(value.ToString()).DoesNotContain("do-not-leak");
    }
}
