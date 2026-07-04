using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NoCTF.Container.K8s;

internal static class KubernetesNames
{
    private static readonly Regex Invalid = new("[^a-z0-9-]", RegexOptions.Compiled);

    public static string SafeName(string value, string fallback = "noctf")
    {
        var lower = Invalid.Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        while (lower.Contains("--", StringComparison.Ordinal))
            lower = lower.Replace("--", "-", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(lower))
            lower = fallback;
        return lower.Length <= 63 ? lower : lower[..63].Trim('-');
    }

    public static string ShortHash(string value, int length = 10)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..length];
    }

    public static string InstanceNamespace(string prefix, string seed)
        => SafeName($"{prefix}-{ShortHash(seed, 12)}", "noctf-inst");
}
