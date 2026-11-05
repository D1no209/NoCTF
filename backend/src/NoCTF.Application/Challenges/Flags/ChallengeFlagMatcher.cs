using System.Text.RegularExpressions;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Challenges.Flags;

public static class ChallengeFlagMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public static bool IsMatch(string submittedFlag, ChallengeFlag candidate) =>
        IsMatch(submittedFlag, candidate.MatchKind, candidate.Flag, candidate.FlagSha256);

    public static bool IsMatch(string submittedFlag, ChallengeFlagMatchKind kind, string candidate, byte[] digest) =>
        kind switch
        {
            ChallengeFlagMatchKind.Exact =>
                DefaultExactMatch(submittedFlag, candidate, digest),
            ChallengeFlagMatchKind.RegularExpression =>
                IsRegularExpressionMatch(submittedFlag, candidate),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    public static bool IsValidRegularExpression(string pattern)
    {
        try
        {
            _ = CreateRegularExpression(pattern);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool DefaultExactMatch(string submittedFlag, string candidate, byte[] digest)
    {
        var submittedHash = ManageChallengeFlags.Hash(submittedFlag);
        return digest is { Length: 32 }
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                submittedHash,
                digest)
            && string.Equals(submittedFlag, candidate, StringComparison.Ordinal);
    }

    private static bool IsRegularExpressionMatch(string submittedFlag, string pattern)
    {
        try
        {
            return CreateRegularExpression(pattern).IsMatch(submittedFlag);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex CreateRegularExpression(string pattern) =>
        new(
            $"\\A(?:{pattern})\\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            MatchTimeout);
}
