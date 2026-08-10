using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.GameplayFact;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpGameplayFactEvaluatorTests
{
    [Test]
    public async Task Break_with_foreign_team_flag_creates_rejected_evidence()
    {
        const string flag = "flag{awdp-foreign}";
        var ownerTeamId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.BreakAttempt,
            Value = flag,
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            OccurredAt = receivedAt
        };
        var context = new GameplayFactProcessingContext(
            submission,
            [],
            [new ChallengeFlag
            {
                Id = Guid.NewGuid(),
                CompetitionChallengeId = submission.CompetitionChallengeId,
                TeamId = ownerTeamId,
                Flag = flag,
                FlagSha256 = submission.ValueSha256,
                CreatedAt = receivedAt
            }],
            null,
            "{}",
            "{}");

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(decision.FailureCode)
            .IsEqualTo(GameplayFactFailureCode.ForeignTeamFlagDetected);
        await Assert.That(decision.VictimTeamId).IsEqualTo(ownerTeamId);
    }

    [Test]
    public async Task Accepted_fix_after_prior_correct_fix_still_requires_runner()
    {
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.FixAttempt,
            OccurredAt = DateTimeOffset.UtcNow
        };
        var priorFact = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = submission.CompetitionId,
            CompetitionChallengeId = submission.CompetitionChallengeId,
            TeamId = submission.TeamId,
            Kind = GameplayFactKind.FixAttempt,
            OccurredAt = submission.OccurredAt.AddSeconds(-1),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct
        };
        var context = new GameplayFactProcessingContext(
            submission,
            [priorFact],
            [],
            null,
            "{}",
            JsonSerializer.Serialize(
                new AwdpChallengeConfiguration(
                    AwdpChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    new AwdpAchievementConfiguration(AchievementSettlement.Milestone, 20),
                    true,
                    10,
                    10),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsNull();
        await Assert.That(decision.FailureCode)
            .IsEqualTo(GameplayFactFailureCode.CheckerPlatformError);
    }
}
