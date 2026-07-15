using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NoCTF.Core;

namespace NoCTF.Plugins.QQBot;

internal sealed record QqBotRenderedMessage(string Text, string SegmentsJson, string Digest);

internal sealed partial class QqBotTemplateRenderer
{
    public QqBotRenderedMessage Render(
        QqBotEventType eventType,
        string template,
        IReadOnlyDictionary<string, string?> values,
        bool mentionAll,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new QqBotTemplateRenderException("template_empty");
        if (template.Length > 8000)
            throw new QqBotTemplateRenderException("template_too_long");
        if (maxLength is < 100 or > 10000)
            throw new QqBotTemplateRenderException("message_limit_invalid");

        var allowed = QqBotDefaults.AllowedVariables(eventType).ToHashSet(StringComparer.Ordinal);
        var rendered = PlaceholderRegex().Replace(template, match =>
        {
            var variable = match.Groups[1].Value;
            if (!allowed.Contains(variable))
                throw new QqBotTemplateRenderException($"template_variable_not_allowed:{variable}");
            return SanitizeDynamicText(values.GetValueOrDefault(variable));
        });

        if (rendered.Contains('{') || rendered.Contains('}'))
            throw new QqBotTemplateRenderException("template_expression_not_supported");

        rendered = SanitizeRenderedText(rendered);
        if (rendered.Length == 0)
            throw new QqBotTemplateRenderException("message_empty");
        if (rendered.Length > maxLength)
            throw new QqBotTemplateRenderException("message_too_long");

        var segments = new List<object>(2);
        if (mentionAll)
            segments.Add(new { type = "mention_all", data = new { } });
        segments.Add(new { type = "text", data = new { text = rendered } });
        var segmentsJson = JsonSerializer.Serialize(segments, JsonOptions);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(segmentsJson))).ToLowerInvariant();
        return new QqBotRenderedMessage(rendered, segmentsJson, $"sha256:{digest}");
    }

    public static string SanitizeDynamicText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormC)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var builder = new StringBuilder(Math.Min(normalized.Length, 4096));
        foreach (var character in normalized)
        {
            if (character == '\n')
            {
                builder.Append(character);
                continue;
            }
            if (character == '\t')
            {
                builder.Append(' ');
                continue;
            }
            if (!char.IsControl(character) && character is not '\u2028' and not '\u2029')
                builder.Append(character);
        }
        return LimitNewlines(builder.ToString(), 20).Trim();
    }

    private static string SanitizeRenderedText(string value)
        => LimitNewlines(SanitizeDynamicText(value), 24).Trim();

    private static string LimitNewlines(string value, int maximum)
    {
        var builder = new StringBuilder(value.Length);
        var newlineCount = 0;
        foreach (var character in value)
        {
            if (character == '\n')
            {
                newlineCount++;
                if (newlineCount > maximum)
                    continue;
            }
            builder.Append(character);
        }
        return builder.ToString();
    }

    [GeneratedRegex("\\{([a-z][a-z0-9_]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class QqBotTemplateRenderException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
