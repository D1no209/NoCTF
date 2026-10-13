using NoCTF.GameModes.Awd.Flags;
using NoCTF.GameModes.Flags;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdFlagGeneratorTests
{
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
    private static AwdFlagIdentity Identity => new(Id(1), Id(2), Id(3), Id(4), Id(5), 7, 2);
    private static byte[] Key => Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Test]
    public async Task CanonicalEncodingMatchesIndependentHmacVector()
    {
        // Independently produced with Python uuid.bytes, struct.pack('>qii') and hmac/sha256.
        var flag = AwdFlagGenerator.Generate(AwdFlagGenerator.Default, Key, Identity);
        await Assert.That(flag).IsEqualTo("flag{-wllfbuFF7u5rIvzoazgbT7kex4OSTvo}");
        await Assert.That(AwdFlagGenerator.Generate(AwdFlagGenerator.Default, Key, Identity)).IsEqualTo(flag);
    }

    [Test]
    public async Task EveryIssuanceDimensionSeparatesFlags()
    {
        var baseline = Identity;
        AwdFlagIdentity[] identities =
        [
            baseline, baseline with { CompetitionId = Id(6) }, baseline with { CompetitionChallengeId = Id(6) },
            baseline with { TeamId = Id(6) }, baseline with { RoundId = Id(6) }, baseline with { StoreId = Id(6) },
            baseline with { GenerationSlot = 8 }, baseline with { Ordinal = 3 }, baseline with { KeyVersion = 2 }
        ];
        await Assert.That(identities.Select(identity => AwdFlagGenerator.Generate(AwdFlagGenerator.Default, Key, identity))
            .Distinct(StringComparer.Ordinal).Count()).IsEqualTo(identities.Length);
    }

    [Test]
    [Arguments("[SECURE:15]")]
    [Arguments("[TEAMID]")]
    [Arguments("[RANDOM:8]")]
    [Arguments("[SECURE]\n")]
    [Arguments("[SECURE][UNKNOWN]")]
    [Arguments("[SECURE:99]")]
    public async Task UnsafeTemplatesAreRejected(string body)
    {
        await Assert.That(AwdFlagGenerator.IsValidTemplate(new("flag", body, false))).IsFalse();
    }

    [Test]
    public async Task LiteralWrapperAndMinimumSecureBodyRemainStable()
    {
        var template = new PerTeamFlagTemplate("", "custom<[SECURE:16]>-[ORDINAL]", false);
        await Assert.That(AwdFlagGenerator.IsValidTemplate(template)).IsTrue();
        var result = AwdFlagGenerator.Generate(template, Key, Identity);
        await Assert.That(result.StartsWith("custom<", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.EndsWith(">-2", StringComparison.Ordinal)).IsTrue();
    }
}
