using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeWriteUpPolicyTests
{
    [Test]
    [Arguments(0L, 20, 0L)]
    [Arguments(500L, 20, 100L)]
    [Arguments(1L, 20, 1L)]
    [Arguments(501L, 20, 101L)]
    [Arguments(500L, 0, 0L)]
    [Arguments(500L, 100, 500L)]
    [Arguments(long.MaxValue, 100, long.MaxValue)]
    public async Task Deduction_rounds_up_once_without_overflow(long gross, int percent, long expected) =>
        await Assert.That(ChallengeWriteUpPolicy.Deduction(gross, percent)).IsEqualTo(expected);

    [Test]
    public async Task Content_formats_are_mutually_exclusive()
    {
        await Assert.That(ChallengeWriteUpPolicy.ValidContent(WriteUpFormat.Markdown, "# Solution", null)).IsTrue();
        await Assert.That(ChallengeWriteUpPolicy.ValidContent(WriteUpFormat.Markdown, "  ", null)).IsFalse();
        await Assert.That(ChallengeWriteUpPolicy.ValidContent(WriteUpFormat.Markdown, "solution", Guid.NewGuid())).IsFalse();
        await Assert.That(ChallengeWriteUpPolicy.ValidContent(WriteUpFormat.Pdf, null, Guid.NewGuid())).IsTrue();
        await Assert.That(ChallengeWriteUpPolicy.ValidContent(WriteUpFormat.Pdf, "solution", Guid.NewGuid())).IsFalse();
        await Assert.That(ChallengeWriteUpPolicy.ValidContent(WriteUpFormat.Markdown,
            new string('x', ChallengeWriteUpPolicy.MaximumMarkdownCharacters + 1), null)).IsFalse();
    }

    [Test]
    public async Task Independent_submission_deadline_and_disabled_switch_are_enforced()
    {
        var end = DateTimeOffset.UtcNow;
        await Assert.That(ChallengeWriteUpPolicy.CanSubmit(true, end, 24, end.AddHours(24))).IsTrue();
        await Assert.That(ChallengeWriteUpPolicy.CanSubmit(true, end, 24, end.AddHours(24).AddTicks(1))).IsFalse();
        await Assert.That(ChallengeWriteUpPolicy.CanSubmit(false, end, 24, end)).IsFalse();
        await Assert.That(ChallengeWriteUpPolicy.CanSubmit(true, end, 8761, end)).IsFalse();
    }
}
