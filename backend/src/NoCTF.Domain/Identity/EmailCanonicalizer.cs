namespace NoCTF.Domain.Identity;

/// <summary>Defines the single persisted representation of an email address.</summary>
public static class EmailCanonicalizer
{
    public static string Canonicalize(string value) =>
        value.Trim().ToLowerInvariant();
}
