using System.Security.Cryptography;
using System.Text;
using NoCTF.GameModes.Flags;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class PerTeamFlagGeneratorTests
{
    [Test]
    public async Task Default_template_uses_the_documented_team_hash_contract()
    {
        var secret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var competitionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var challengeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var competitionChallengeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var teamId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var input = Encoding.UTF8.GetBytes(
            $"noctf:teamhash:v1:{competitionId:D}:{competitionChallengeId:D}:{teamId:D}");
        var expectedHash = Convert.ToHexStringLower(HMACSHA256.HashData(secret, input))[..32];

        var flag = PerTeamFlagGenerator.Generate(
            PerTeamFlagTemplate.Default,
            new(secret, competitionId, challengeId, competitionChallengeId, teamId));

        await Assert.That(flag).IsEqualTo($"flag{{{expectedHash}}}");
    }

    [Test]
    public async Task Specification_scope_is_stable_within_a_round_and_rotates_between_rounds()
    {
        var context = new PerTeamFlagContext(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("00000001-0000-0000-0000-000000000000"));

        var first = PerTeamFlagGenerator.Generate(PerTeamFlagTemplate.Default, context);
        var replay = PerTeamFlagGenerator.Generate(PerTeamFlagTemplate.Default, context);
        var next = PerTeamFlagGenerator.Generate(
            PerTeamFlagTemplate.Default,
            context with { SpecificationId = Guid.Parse("00000002-0000-0000-0000-000000000000") });

        await Assert.That(replay).IsEqualTo(first);
        await Assert.That(next).IsNotEqualTo(first);
    }

    [Test]
    public async Task Leet_only_changes_literal_text_and_preserves_placeholder_values()
    {
        var teamId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var flag = PerTeamFlagGenerator.Generate(
            new("ctf", "abcdef[TEAMID:N]", true),
            new(
                new byte[32],
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                teamId));

        await Assert.That(flag).StartsWith("ctf{");
        await Assert.That(flag).EndsWith($"{teamId:N}}}");
        await Assert.That(flag[..^33]).IsNotEqualTo("ctf{abcdef");
    }

    [Test]
    public async Task Unknown_placeholder_is_rejected()
    {
        var action = () => PerTeamFlagGenerator.Generate(
            new("", "[UNKNOWN]", false),
            new(new byte[32], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        await Assert.That(action).Throws<FormatException>();
    }
}
