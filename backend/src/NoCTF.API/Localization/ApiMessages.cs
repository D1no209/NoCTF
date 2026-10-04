using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NoCTF.API.Localization;

public readonly record struct ApiMessage(
    ApiMessageId Id,
    IReadOnlyDictionary<string, object?>? Arguments = null)
{
    public string Key => ApiMessages.Key(Id);
    public string Text => ApiMessages.Text(Id, Arguments);
}

/// <summary>Protocol text is selected only at the HTTP boundary, never in application rules.</summary>
public static partial class ApiMessages
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Catalogs =
        new(LoadCatalogs);
    private static readonly Lazy<IReadOnlyDictionary<string, ApiMessageId>> MessageIds =
        new(() => Enum.GetValues<ApiMessageId>().ToDictionary(Key, id => id, StringComparer.Ordinal));
    public static readonly IReadOnlyDictionary<string, object?> NoArguments =
        new Dictionary<string, object?>();

    public static void Initialize() => _ = Catalogs.Value;
    public static ApiMessageId? FindKey(string? key) =>
        key is not null && MessageIds.Value.TryGetValue(key, out var id) ? id : null;
    public static ApiMessage Get(ApiMessageId id, IReadOnlyDictionary<string, object?>? arguments = null) => new(id, arguments);
    public static IReadOnlyDictionary<string, object?> RequiredArguments(
        ApiMessageId id, IReadOnlyDictionary<string, object?> values) => Parameters(Catalogs.Value["en"][Key(id)])
        .Where(values.ContainsKey).ToDictionary(key => key, key => values[key], StringComparer.Ordinal);

    public static string Text(ApiMessageId id, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var key = Key(id);
        var catalog = Catalogs.Value[CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN" : "en"];
        var template = catalog.TryGetValue(key, out var translated) && !string.IsNullOrWhiteSpace(translated)
            ? translated : Catalogs.Value["en"][key];
        return Placeholder().Replace(template, match =>
            arguments is not null && arguments.TryGetValue(match.Groups[1].Value, out var value)
                ? Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty : match.Value);
    }

    // Positional failure DTO constructors remain transport compatible. Their supplied diagnostic
    // text is deliberately not used to identify a failure or to translate it.
    public static string Localize(Enum code, string? diagnostic, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        _ = diagnostic;
        var message = For(code);
        return Text(message.Id, arguments);
    }

    public static string Localize(ApiMessageId id, string? diagnostic)
    {
        _ = diagnostic;
        return Text(id);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadCatalogs()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var locale in new[] { "en", "zh-CN" })
        {
            using var stream = typeof(ApiMessages).Assembly.GetManifestResourceStream(
                $"NoCTF.API.Localization.Catalogs.{locale}.api.json")
                ?? throw new InvalidOperationException($"Missing HTTP translation catalog: {locale}");
            using var document = JsonDocument.Parse(stream);
            var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || !catalog.TryAdd(property.Name, property.Value.GetString()!))
                    throw new InvalidOperationException($"Invalid or duplicate HTTP translation key: {property.Name}");
            }
            result.Add(locale, catalog);
        }
        foreach (var (key, translation) in result["zh-CN"])
        {
            if (!result["en"].TryGetValue(key, out var source))
                throw new InvalidOperationException($"Unknown HTTP translation key: {key}");
            if (!string.IsNullOrWhiteSpace(translation)
                && !Parameters(source).SequenceEqual(Parameters(translation)))
                throw new InvalidOperationException($"HTTP translation parameters differ: {key}");
        }
        foreach (var id in Enum.GetValues<ApiMessageId>())
            if (!result["en"].TryGetValue(Key(id), out var text) || string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"Missing English HTTP translation: {id}");
        return result;
    }

    private static IEnumerable<string> Parameters(string text) => Placeholder().Matches(text)
        .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
