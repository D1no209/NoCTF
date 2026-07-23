using NoCTF.Domain.Challenges;

namespace NoCTF.Tests.Unit.Domain;

public sealed class AwdRoundSpecificationIdTests
{
    [Test]
    [Arguments(1, "00000001-0000-0000-0000-000000000000")]
    [Arguments(132, "00000132-0000-0000-0000-000000000000")]
    [Arguments(99_999_999, "99999999-0000-0000-0000-000000000000")]
    public async Task FromRound_UsesDecimalGuidTextEncoding(int round, string expected)
    {
        var id = AwdRoundSpecificationId.FromRound(round);

        await Assert.That(id.Value.ToString("D")).IsEqualTo(expected);
        await Assert.That(id.Round).IsEqualTo(round);
    }

    [Test]
    [Arguments(0)]
    [Arguments(100_000_000)]
    public async Task FromRound_RejectsOutOfRangeValue(int round)
    {
        await Assert.That(() => AwdRoundSpecificationId.FromRound(round))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Parse_RejectsNonCanonicalGuid()
    {
        await Assert.That(() => AwdRoundSpecificationId.Parse(
                Guid.Parse("00000001-0000-0000-0000-000000000001")))
            .Throws<FormatException>();
    }
}
