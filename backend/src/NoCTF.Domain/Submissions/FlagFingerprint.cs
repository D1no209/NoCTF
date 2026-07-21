using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Domain.Submissions;

public readonly record struct FlagFingerprint(string Sha256, int Length)
{
    public static FlagFingerprint Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), value.Length);
    }

    public bool Matches(string value)
    {
        var candidate = Create(value);
        return Length == candidate.Length
               && CryptographicOperations.FixedTimeEquals(
                   Convert.FromHexString(Sha256),
                   Convert.FromHexString(candidate.Sha256));
    }
}
