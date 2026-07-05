using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// Timing-safe flag comparison to prevent timing attacks.
/// </summary>
public static class FlagValidator
{
    /// <summary>
    /// Compares two flag strings using a constant-time algorithm to prevent timing attacks.
    /// </summary>
    /// <param name="submitted">The flag submitted by the user.</param>
    /// <param name="expected">The expected correct flag.</param>
    /// <returns>True if the flags match; otherwise false.</returns>
    public static bool IsMatch(string submitted, string expected)
    {
        if (string.IsNullOrWhiteSpace(submitted) || string.IsNullOrWhiteSpace(expected))
            return false;

        var submittedBytes = Encoding.UTF8.GetBytes(submitted.Trim());
        var expectedBytes = Encoding.UTF8.GetBytes(expected.Trim());

        return submittedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(submittedBytes, expectedBytes);
    }
}
