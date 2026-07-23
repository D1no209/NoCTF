using System.Globalization;

namespace NoCTF.Domain.Challenges;

/// <summary>Canonical Guid encoding of the 1-based AWD round number.</summary>
public readonly record struct AwdRoundSpecificationId
{
    public const int MaximumRound = 99_999_999;
    private const string GuidSuffix = "-0000-0000-0000-000000000000";

    private AwdRoundSpecificationId(Guid value, int round)
    {
        Value = value;
        Round = round;
    }

    public Guid Value { get; }
    public int Round { get; }

    public static AwdRoundSpecificationId FromRound(int round)
    {
        if (round is < 1 or > MaximumRound)
            throw new ArgumentOutOfRangeException(nameof(round), round, "AWD round must be between 1 and 99,999,999.");

        var text = round.ToString("D8", CultureInfo.InvariantCulture) + GuidSuffix;
        return new AwdRoundSpecificationId(Guid.ParseExact(text, "D"), round);
    }

    public static AwdRoundSpecificationId Parse(Guid value)
    {
        var text = value.ToString("D", CultureInfo.InvariantCulture);
        if (!text.EndsWith(GuidSuffix, StringComparison.Ordinal)
            || !int.TryParse(text.AsSpan(0, 8), NumberStyles.None, CultureInfo.InvariantCulture, out var round)
            || round is < 1 or > MaximumRound)
            throw new FormatException("The Guid is not a canonical AWD round specification id.");

        var result = FromRound(round);
        if (result.Value != value)
            throw new FormatException("The Guid is not a canonical AWD round specification id.");
        return result;
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
