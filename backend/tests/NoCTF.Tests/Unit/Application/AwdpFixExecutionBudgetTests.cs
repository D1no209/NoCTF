using NoCTF.Application.GameplayFacts.Awdp;

namespace NoCTF.Tests.Unit.Application;

public sealed class AwdpFixExecutionBudgetTests
{
    [Test]
    public async Task Deadline_uses_upload_time_and_the_effective_execution_budget()
    {
        var uploadedAt = DateTimeOffset.Parse("2026-09-01T10:00:00+00:00");

        var deadline = AwdpFixExecutionBudget.CalculateDeadline(
            uploadedAt,
            uploadedAt.AddHours(1),
            AwdpFixExecutionBudget.DefaultPatchTimeoutSeconds,
            60);

        await Assert.That(deadline).IsEqualTo(uploadedAt.AddMinutes(4));
    }

    [Test]
    public async Task Runtime_expiry_caps_the_execution_deadline()
    {
        var uploadedAt = DateTimeOffset.Parse("2026-09-01T10:00:00+00:00");
        var expiresAt = uploadedAt.AddMinutes(2);

        var deadline = AwdpFixExecutionBudget.CalculateDeadline(
            uploadedAt,
            expiresAt,
            180,
            180);

        await Assert.That(deadline).IsEqualTo(expiresAt);
    }

    [Test]
    public async Task Dedicated_handler_timeout_exceeds_the_maximum_valid_budget()
    {
        var maximumBudget = AwdpFixExecutionBudget.Calculate(
            AwdpFixExecutionBudget.MaximumPatchTimeoutSeconds,
            AwdpFixExecutionBudget.MaximumCheckerTimeoutSeconds);
        var handlerTimeout = AwdpFixExecutionBudget.HandlerExecutionTimeoutSeconds;
        var maximumAckExtension =
            AwdpFixExecutionBudget.JetStreamMaximumAckExtensionSeconds;

        await Assert.That(TimeSpan.FromSeconds(handlerTimeout))
            .IsGreaterThan(maximumBudget);
        await Assert.That(maximumAckExtension).IsGreaterThan(handlerTimeout);
    }
}
