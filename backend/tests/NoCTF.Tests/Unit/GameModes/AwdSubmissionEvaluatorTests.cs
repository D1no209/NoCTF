using System.Text.Json;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Submission;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdSubmissionEvaluatorTests
{
    [Test]
    public async Task Attack_is_rejected_while_pause_aware_hardening_clock_is_active()
    {
        var now = DateTimeOffset.UtcNow;
        var competition = AwdConfiguration.Default with { HardeningDurationSeconds = 600 };
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = SubmissionKind.Flag,
            ReceivedAt = now
        };
        var context = new SubmissionProcessingContext(
            submission,
            [],
            [],
            null,
            JsonSerializer.Serialize(competition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(new AwdChallengeConfiguration(AwdChallengeConfiguration.CurrentSchemaVersion),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            EffectiveRunningTime: TimeSpan.FromMinutes(3));

        var decision = new AwdSubmissionEvaluator().Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(decision.FailureCode).IsEqualTo(ScoringFailureCode.HardeningActive);
    }
}
