using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.GameplayFact;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdGameplayFactEvaluatorTests
{
    [Test]
    public async Task Foreign_team_flag_remains_a_normal_awd_attack()
    {
        const string flag = "flag{awd-attack}";
        var ownerTeamId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.FlagAttempt,
            Value = flag,
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            OccurredAt = receivedAt
        };
        var competition = AwdConfiguration.Default with { HardeningDurationSeconds = 0 };
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
            JsonSerializer.Serialize(competition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(
                new AwdChallengeConfiguration(AwdChallengeConfiguration.CurrentSchemaVersion),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            EffectiveRunningTime: TimeSpan.FromHours(1));

        var decision = new AwdGameplayFactEvaluator().Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(decision.FailureCode).IsNull();
        await Assert.That(decision.VictimTeamId).IsEqualTo(ownerTeamId);
    }

    [Test]
    public async Task Attack_is_rejected_while_pause_aware_hardening_clock_is_active()
    {
        var now = DateTimeOffset.UtcNow;
        var competition = AwdConfiguration.Default with { HardeningDurationSeconds = 600 };
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.FlagAttempt,
            OccurredAt = now
        };
        var context = new GameplayFactProcessingContext(
            submission,
            [],
            [],
            null,
            JsonSerializer.Serialize(competition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(new AwdChallengeConfiguration(AwdChallengeConfiguration.CurrentSchemaVersion),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            EffectiveRunningTime: TimeSpan.FromMinutes(3));

        var decision = new AwdGameplayFactEvaluator().Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(decision.FailureCode).IsEqualTo(GameplayFactFailureCode.HardeningActive);
    }
}
