using System.Numerics;

namespace NoCTF.GameModes.Awd.Scoring;

/// <summary>Exact linear budget arithmetic, so repeating thirds cannot lose an integer point.</summary>
public readonly record struct AwdScoreFraction
{
    public BigInteger Numerator { get; }
    private readonly BigInteger denominator;
    public BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;

    public AwdScoreFraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        this.denominator = denominator / divisor;
    }

    public bool Equals(AwdScoreFraction other) => Numerator == other.Numerator && Denominator == other.Denominator;
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public static implicit operator AwdScoreFraction(decimal value)
    {
        var bits = decimal.GetBits(value);
        var magnitude = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        if ((bits[3] & int.MinValue) != 0) magnitude = -magnitude;
        return new(magnitude, BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
    }

    public static AwdScoreFraction operator +(AwdScoreFraction left, AwdScoreFraction right) =>
        new(left.Numerator * right.Denominator + right.Numerator * left.Denominator, left.Denominator * right.Denominator);
    public static AwdScoreFraction operator -(AwdScoreFraction left, AwdScoreFraction right) =>
        new(left.Numerator * right.Denominator - right.Numerator * left.Denominator, left.Denominator * right.Denominator);
    public static AwdScoreFraction operator *(AwdScoreFraction left, AwdScoreFraction right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    public static AwdScoreFraction operator /(AwdScoreFraction left, AwdScoreFraction right) =>
        right.Numerator <= 0 ? throw new DivideByZeroException() :
            new(left.Numerator * right.Denominator, left.Denominator * right.Numerator);
    public static bool operator <(AwdScoreFraction left, AwdScoreFraction right) =>
        left.Numerator * right.Denominator < right.Numerator * left.Denominator;
    public static bool operator >(AwdScoreFraction left, AwdScoreFraction right) => right < left;
    public static bool operator <=(AwdScoreFraction left, AwdScoreFraction right) => !(left > right);
    public static bool operator >=(AwdScoreFraction left, AwdScoreFraction right) => !(left < right);

    public long Truncate() => checked((long)(Numerator / Denominator));

    /// <summary>Bounded display approximation. Never use it to compute final integer totals.</summary>
    public decimal ToDecimal()
    {
        var integer = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        const decimal scale = 1_000_000_000_000m;
        return checked((decimal)integer + (decimal)(remainder * (BigInteger)scale / Denominator) / scale);
    }
}
