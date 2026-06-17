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
        if (submitted is null || expected is null)
            return false;

        var submittedBytes = Encoding.UTF8.GetBytes(submitted);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return CryptographicOperations.FixedTimeEquals(submittedBytes, expectedBytes);
    }
}
