namespace NoCTF.PluginBase;

using System.Data.Common;
using System.Text;

public static class SecretValueValidator
{
    private static readonly string[] PlaceholderFragments =
    [
        "replace-with",
        "change-me",
        "changeme",
        "your-",
        "your-super-secret",
        "example-secret",
        "default-password",
        "noctf_password",
        "admin@123456",
        "minioadmin",
        "dev-runner-token"
    ];

    private static readonly string[] NormalizedPlaceholderFragments = PlaceholderFragments
        .Select(NormalizeForPlaceholderComparison)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static bool IsUnsafe(string? value, int minimumLength = 16)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < minimumLength)
            return true;

        var normalized = NormalizeForPlaceholderComparison(value);
        return normalized.Trim('-').Length == 0 ||
               NormalizedPlaceholderFragments.Any(fragment =>
                   normalized.Contains(fragment, StringComparison.Ordinal));
    }

    public static void RequireSafe(string name, string? value, int minimumLength = 16)
    {
        if (IsUnsafe(value, minimumLength))
            throw new InvalidOperationException($"{name} must be configured with a non-placeholder secret of at least {minimumLength} characters.");
    }

    public static bool IsConnectionStringUnsafe(string? connectionString, int minimumPasswordLength = 16)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return true;
        try
        {
            var values = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var password = values.ContainsKey("Password")
                ? values["Password"]?.ToString()
                : values.ContainsKey("Pwd")
                    ? values["Pwd"]?.ToString()
                    : null;
            return IsUnsafe(password, minimumPasswordLength);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static string NormalizeForPlaceholderComparison(string value)
    {
        var result = new StringBuilder(value.Length);
        var previousWasSeparator = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                result.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                result.Append('-');
                previousWasSeparator = true;
            }
        }

        return result.ToString();
    }
}
