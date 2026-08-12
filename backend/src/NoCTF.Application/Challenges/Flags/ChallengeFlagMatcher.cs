using System.Text.RegularExpressions;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Challenges.Flags;

public static class ChallengeFlagMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public static bool IsMatch(string submittedFlag, ChallengeFlag candidate) =>
        candidate.MatchKind switch
        {
            ChallengeFlagMatchKind.Exact =>
                DefaultExactMatch(submittedFlag, candidate),
            ChallengeFlagMatchKind.RegularExpression =>
                IsRegularExpressionMatch(submittedFlag, candidate.Flag),
            _ => false
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

    private static bool DefaultExactMatch(string submittedFlag, ChallengeFlag candidate)
    {
        var submittedHash = ManageChallengeFlags.Hash(submittedFlag);
        return candidate.FlagSha256 is { Length: 32 }
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                submittedHash,
                candidate.FlagSha256)
            && string.Equals(submittedFlag, candidate.Flag, StringComparison.Ordinal);
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
