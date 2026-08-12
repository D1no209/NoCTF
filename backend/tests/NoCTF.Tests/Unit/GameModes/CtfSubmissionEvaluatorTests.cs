using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.GameplayFact;
using NoCTF.GameModes.Ctf.Configuration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CtfGameplayFactEvaluatorTests
{
    [Test]
    public async Task Static_ctf_regular_expression_matches_the_entire_flag_case_sensitively()
    {
        var fixture = CreateFixture("flag{123e4567-e89b-12d3-a456-426614174000}");
        var expression = fixture.Flag(null);
        expression.Flag = @"flag\{[0-9a-f-]{36}\}";
        expression.FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(expression.Flag));
        expression.MatchKind = ChallengeFlagMatchKind.RegularExpression;

        var staticDefinition = JsonSerializer.Serialize(new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            null,
            null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var accepted = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(fixture.Context([expression]) with { ChallengeDefinitionJson = staticDefinition });
        fixture.GameplayFact.Value = "prefix-flag{123e4567-e89b-12d3-a456-426614174000}";
        var partial = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(fixture.Context([expression]) with { ChallengeDefinitionJson = staticDefinition });
        fixture.GameplayFact.Value = "FLAG{123e4567-e89b-12d3-a456-426614174000}";
        var differentCase = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(fixture.Context([expression]) with { ChallengeDefinitionJson = staticDefinition });

        await Assert.That(accepted.Result).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(partial.Result).IsEqualTo(GameplayFactResult.Wrong);
        await Assert.That(differentCase.Result).IsEqualTo(GameplayFactResult.Wrong);
    }

    [Test]
    public async Task Dynamic_ctf_runtime_ignores_regular_expression_flags()
    {
        var fixture = CreateFixture("flag{dynamic}");
        var expression = fixture.Flag(null);
        expression.Flag = @"flag\{.*\}";
        expression.FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(expression.Flag));
        expression.MatchKind = ChallengeFlagMatchKind.RegularExpression;
        var definition = JsonSerializer.Serialize(new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            null,
            null,
            Runtime: new ChallengeRuntimeTemplate(
                RuntimeAllocation.PerTeam,
                new ContainerRuntimeDefinition("registry.example/challenge:v1"),
                FlagSource: RuntimeFlagSource.PerTeam)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(fixture.Context([expression]) with { ChallengeDefinitionJson = definition });

        await Assert.That(result.Result).IsEqualTo(GameplayFactResult.Wrong);
    }

    [Test]
    public async Task Foreign_team_flag_is_rejected_with_owner_team_evidence()
    {
        var fixture = CreateFixture("flag{foreign}");
        var ownerTeamId = Guid.NewGuid();
        var context = fixture.Context([
            fixture.Flag(ownerTeamId)
        ]);

        var result = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(result.FailureCode)
            .IsEqualTo(GameplayFactFailureCode.ForeignTeamFlagDetected);
        await Assert.That(result.VictimTeamId).IsEqualTo(ownerTeamId);
    }

    [Test]
    public async Task Own_or_public_flag_takes_precedence_over_foreign_match()
    {
        var fixture = CreateFixture("flag{shared}");
        var context = fixture.Context([
            fixture.Flag(Guid.NewGuid()),
            fixture.Flag(null)
        ]);

        var result = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.VictimTeamId).IsNull();
    }

    [Test]
    public async Task Expired_foreign_flag_is_an_ordinary_wrong_answer()
    {
        var fixture = CreateFixture("flag{expired}");
        var expired = fixture.Flag(Guid.NewGuid());
        expired.ValidUntil = fixture.OccurredAt;
        var context = fixture.Context([expired]);

        var result = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(GameplayFactResult.Wrong);
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.VictimTeamId).IsNull();
    }

    [Test]
    public async Task Shared_foreign_flag_is_rejected_without_selecting_an_owner_team()
    {
        var fixture = CreateFixture("flag{ambiguous}");
        var context = fixture.Context([
            fixture.Flag(Guid.NewGuid()),
            fixture.Flag(Guid.NewGuid())
        ]);

        var result = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(result.FailureCode)
            .IsEqualTo(GameplayFactFailureCode.AmbiguousFlagMatch);
        await Assert.That(result.VictimTeamId).IsNull();
    }

    [Test]
    public async Task Hint_unlock_does_not_turn_a_correct_rejudge_into_a_duplicate()
    {
        const string flag = "flag{rejudge}";
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(flag));
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            CompetitionChallengeId = challengeId,
            TeamId = teamId,
            Kind = GameplayFactKind.FlagAttempt,
            Value = flag,
            ValueSha256 = hash,
            OccurredAt = receivedAt
        };
        var hintUnlock = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            CompetitionChallengeId = challengeId,
            TeamId = teamId,
            Kind = GameplayFactKind.HintUnlock,
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Unlocked,
            OccurredAt = receivedAt,
            UpdatedAt = receivedAt
        };
        var challengeFlag = new ChallengeFlag
        {
            Id = Guid.NewGuid(),
            CompetitionChallengeId = challengeId,
            TeamId = teamId,
            Flag = flag,
            FlagSha256 = hash,
            CreatedAt = receivedAt
        };
        var context = new GameplayFactProcessingContext(
            submission,
            [hintUnlock],
            [challengeFlag],
            null,
            "{}",
            "{}");

        var result = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(GameplayFactResult.Correct);
    }

    private static Fixture CreateFixture(string flag)
    {
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
        return new(submission, receivedAt);
    }

    private sealed record Fixture(GameplayFact GameplayFact, DateTimeOffset OccurredAt)
    {
        public GameplayFactProcessingContext Context(IReadOnlyList<ChallengeFlag> flags) =>
            new(GameplayFact, [], flags, null, "{}", "{}");

        public ChallengeFlag Flag(Guid? teamId) => new()
        {
            Id = Guid.NewGuid(),
            CompetitionChallengeId = GameplayFact.CompetitionChallengeId,
            TeamId = teamId,
            Flag = GameplayFact.Value!,
            FlagSha256 = GameplayFact.ValueSha256!,
            CreatedAt = OccurredAt
        };
    }
}
