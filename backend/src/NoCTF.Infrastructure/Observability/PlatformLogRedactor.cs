using System.Text.RegularExpressions;

namespace NoCTF.Infrastructure.Observability;

public static partial class PlatformLogRedactor
{
    private const string Redacted = "[REDACTED]";

    public static string Redact(
        string? value,
        IEnumerable<KeyValuePair<string, object?>> properties)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var result = value;
        foreach (var property in properties)
        {
            if (!IsSensitiveKey(property.Key) || property.Value is null)
                continue;
            var sensitiveValue = Convert.ToString(
                property.Value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(sensitiveValue))
                result = result.Replace(sensitiveValue, Redacted, StringComparison.Ordinal);
        }

        result = TotpUriRegex().Replace(result, Redacted);
        result = BearerTokenRegex().Replace(result, "Bearer " + Redacted);
        result = JwtRegex().Replace(result, Redacted);
        result = FlagPayloadRegex().Replace(result, Redacted);
        result = HttpUriQueryRegex().Replace(result, match =>
            match.Groups[1].Value + "?" + Redacted);
        result = UriUserInfoRegex().Replace(result, match =>
            match.Groups[1].Value + Redacted + "@");
        result = SecretAssignmentRegex().Replace(result, match =>
            match.Groups[1].Value + "=" + Redacted);
        return result;
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = new string(key
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("flag", StringComparison.Ordinal)
            || normalized.Contains("passwd", StringComparison.Ordinal)
            || normalized.Equals("pwd", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("cookie", StringComparison.Ordinal)
            || normalized.Contains("userid", StringComparison.Ordinal)
            || normalized.Contains("smtpuser", StringComparison.Ordinal)
            || normalized.Equals("code", StringComparison.Ordinal)
            || normalized.Contains("recoverycode", StringComparison.Ordinal)
            || normalized.Contains("totp", StringComparison.Ordinal)
            || normalized.Contains("provisioninguri", StringComparison.Ordinal);
    }

    [GeneratedRegex(
        "(?i)\\bBearer\\s+[A-Za-z0-9._~+\\-/]+=*",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(
        "\\beyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(
        "(?i)\\b(?:[a-z0-9_]*ctf|flag)\\{[^{}\\r\\n]{1,256}\\}",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex FlagPayloadRegex();

    [GeneratedRegex(
        "(?i)\\b(https?://[^\\s?#]+)\\?[^\\s#]*",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex HttpUriQueryRegex();

    [GeneratedRegex(
        "(?i)\\b(smtps?://)[^@\\s]+@",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex UriUserInfoRegex();

    [GeneratedRegex(
        "(?i)(\\\"?(?:password|passwd|pwd|code|totp|recovery[_-]?code|provisioning[_-]?uri|(?:access[_-]?|refresh[_-]?|internal[_-]?)?token|(?:client[_-]?)?secret|credential|authorization|cookie|smtp(?:user(?:name)?|password)?)\\\"?)\\s*[:=]\\s*(?:\\\"[^\\\"]*\\\"|'[^']*'|[^\\s,;]+)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex("(?i)otpauth://[^\\s\"<>]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex TotpUriRegex();
}
