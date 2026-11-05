using NoCTF.Application.Challenges.Capabilities;

namespace NoCTF.Tests.Unit.Application;

public sealed class FixExecutionBudgetTests
{
    [Test]
    public async Task Deadline_uses_upload_time_and_the_effective_execution_budget()
    {
        var uploadedAt = DateTimeOffset.Parse("2026-09-01T10:00:00+00:00");

        var deadline = FixExecutionBudget.CalculateDeadline(
            uploadedAt,
            uploadedAt.AddHours(1),
            FixExecutionBudget.DefaultPatchTimeoutSeconds,
            60);

        await Assert.That(deadline).IsEqualTo(uploadedAt.AddMinutes(4));
    }

    [Test]
    public async Task Runtime_expiry_caps_the_execution_deadline()
    {
        var uploadedAt = DateTimeOffset.Parse("2026-09-01T10:00:00+00:00");
        var expiresAt = uploadedAt.AddMinutes(2);

        var deadline = FixExecutionBudget.CalculateDeadline(
            uploadedAt,
            expiresAt,
            180,
            180);

        await Assert.That(deadline).IsEqualTo(expiresAt);
    }

    [Test]
    public async Task Dedicated_handler_timeout_exceeds_the_maximum_valid_budget()
    {
        var maximumBudget = FixExecutionBudget.Calculate(
            FixExecutionBudget.MaximumPatchTimeoutSeconds,
            FixExecutionBudget.MaximumCheckerTimeoutSeconds);
        var handlerTimeout = FixExecutionBudget.HandlerExecutionTimeoutSeconds;
        var maximumAckExtension =
            FixExecutionBudget.JetStreamMaximumAckExtensionSeconds;

        await Assert.That(TimeSpan.FromSeconds(handlerTimeout))
            .IsGreaterThan(maximumBudget);
        await Assert.That(maximumAckExtension).IsGreaterThan(handlerTimeout);
    }
}
