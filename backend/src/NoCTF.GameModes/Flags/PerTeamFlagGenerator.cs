using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NoCTF.GameModes.Flags;

public sealed record PerTeamFlagTemplate(
    string Header,
    string BodyTemplate,
    bool LeetLiteralText)
{
    public static PerTeamFlagTemplate Default { get; } = new("flag", "[TEAMHASH]", false);
}

public readonly record struct PerTeamFlagContext(
    byte[] FlagDerivationSecret,
    Guid CompetitionId,
    Guid ChallengeId,
    Guid CompetitionChallengeId,
    Guid TeamId);

public static partial class PerTeamFlagGenerator
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private static readonly IReadOnlyDictionary<char, string> LeetCandidates =
        new Dictionary<char, string>
        {
            ['a'] = "A4", ['b'] = "B8", ['c'] = "CkK", ['d'] = "D", ['e'] = "E3",
            ['f'] = "F", ['g'] = "GqQ69", ['h'] = "H", ['i'] = "IlL1", ['j'] = "J",
            ['k'] = "KcC", ['l'] = "LiI1", ['m'] = "MnN", ['n'] = "NmM", ['o'] = "O0",
            ['p'] = "P", ['q'] = "QgG9", ['r'] = "R", ['s'] = "S5", ['t'] = "T7",
            ['u'] = "UvV", ['v'] = "VuU", ['w'] = "W", ['x'] = "X", ['y'] = "Y",
            ['z'] = "Z2", ['0'] = "89oO", ['1'] = "7iIlL", ['2'] = "7zZ", ['3'] = "8eE",
            ['4'] = "9aA", ['5'] = "6sS", ['6'] = "589gG", ['7'] = "12tT", ['8'] = "0369bB",
            ['9'] = "0468gGqQ"
        };

    public static string Generate(PerTeamFlagTemplate template, PerTeamFlagContext context)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (context.FlagDerivationSecret.Length != 32)
            throw new ArgumentException("Flag derivation secret must contain exactly 32 bytes.", nameof(context));

        var generatedGuid = Guid.NewGuid();
        var segments = ParseSegments(template.BodyTemplate, context, generatedGuid);
        if (template.LeetLiteralText)
            ApplySafeLeet(segments);

        var body = string.Concat(segments.Select(segment => segment.Text));
        var flag = string.IsNullOrEmpty(template.Header)
            ? body
            : $"{template.Header}{{{body}}}";
        var byteCount = Encoding.UTF8.GetByteCount(flag);
        if (byteCount is < 1 or > 4096 || flag.Contains('\0', StringComparison.Ordinal))
            throw new FormatException("Generated flag must be 1..4096 UTF-8 bytes and contain no NUL.");
        return flag;
    }

    public static bool IsValidTemplate(PerTeamFlagTemplate template)
    {
        try
        {
            _ = Generate(template, new(
                new byte[32],
                Guid.Empty,
                Guid.Empty,
                Guid.Empty,
                Guid.Empty));
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static List<TemplateSegment> ParseSegments(
        string template,
        PerTeamFlagContext context,
        Guid generatedGuid)
    {
        var segments = new List<TemplateSegment>();
        var position = 0;
        foreach (Match match in PlaceholderRegex().Matches(template))
        {
            if (match.Index > position)
                segments.Add(new(template[position..match.Index], true));
            segments.Add(new(Expand(match.Value[1..^1], context, generatedGuid), false));
            position = match.Index + match.Length;
        }
        if (position < template.Length)
            segments.Add(new(template[position..], true));
        if (segments.Where(segment => segment.IsLiteral)
            .Any(segment => segment.Text.Contains('[', StringComparison.Ordinal)
                || segment.Text.Contains(']', StringComparison.Ordinal)))
            throw new FormatException("Flag template contains an invalid placeholder.");
        return segments;
    }

    private static string Expand(string token, PerTeamFlagContext context, Guid generatedGuid)
    {
        var separator = token.IndexOf(':');
        var name = separator < 0 ? token : token[..separator];
        var format = separator < 0 ? null : token[(separator + 1)..];
        return name switch
        {
            "GUID" => FormatGuid(generatedGuid, format),
            "TEAMID" => FormatGuid(context.TeamId, format),
            "CHALLENGEID" => FormatGuid(context.ChallengeId, format),
            "COMPETITIONCHALLENGEID" => FormatGuid(context.CompetitionChallengeId, format),
            "COMPETITIONID" => FormatGuid(context.CompetitionId, format),
            "TEAMHASH" => TeamHash(context, ParseLength(format, 32, 8, 64)),
            "RANDOM" => RandomBase62(ParseRequiredLength(format, 8, 128)),
            _ => throw new FormatException($"Unknown flag-template placeholder '{name}'.")
        };
    }

    private static string FormatGuid(Guid value, string? format) => format switch
    {
        null or "D" => value.ToString("D"),
        "N" => value.ToString("N"),
        _ => throw new FormatException($"Unknown GUID format '{format}'.")
    };

    private static int ParseLength(string? value, int defaultValue, int minimum, int maximum) =>
        value is null ? defaultValue : ParseRequiredLength(value, minimum, maximum);

    private static int ParseRequiredLength(string? value, int minimum, int maximum)
    {
        if (!int.TryParse(value, out var length) || length < minimum || length > maximum)
            throw new FormatException($"Placeholder length must be between {minimum} and {maximum}.");
        return length;
    }

    private static string TeamHash(PerTeamFlagContext context, int length)
    {
        var message = Encoding.UTF8.GetBytes(
            $"noctf:teamhash:v1:{context.CompetitionId:D}:{context.CompetitionChallengeId:D}:{context.TeamId:D}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(context.FlagDerivationSecret, message))[..length];
    }

    private static string RandomBase62(int length)
    {
        return string.Create(length, 0, static (span, _) =>
        {
            for (var index = 0; index < span.Length; index++)
                span[index] = Base62[RandomNumberGenerator.GetInt32(Base62.Length)];
        });
    }

    private static void ApplySafeLeet(List<TemplateSegment> segments)
    {
        var changed = false;
        var replaceable = new List<(int Segment, int Character, char Original, string Candidates)>();
        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if (!segment.IsLiteral)
                continue;
            var characters = segment.Text.ToCharArray();
            for (var characterIndex = 0; characterIndex < characters.Length; characterIndex++)
            {
                var original = characters[characterIndex];
                if (!LeetCandidates.TryGetValue(char.ToLowerInvariant(original), out var candidates))
                    continue;
                replaceable.Add((segmentIndex, characterIndex, original, candidates));
                if (RandomNumberGenerator.GetInt32(2) == 0)
                    continue;
                var replacement = candidates[RandomNumberGenerator.GetInt32(candidates.Length)];
                characters[characterIndex] = replacement;
                changed |= replacement != original;
            }
            segments[segmentIndex] = segment with { Text = new string(characters) };
        }
        if (changed || replaceable.Count == 0)
            return;

        var forced = replaceable[RandomNumberGenerator.GetInt32(replaceable.Count)];
        var alternatives = forced.Candidates.Where(candidate => candidate != forced.Original).ToArray();
        if (alternatives.Length == 0)
            return;
        var forcedCharacters = segments[forced.Segment].Text.ToCharArray();
        forcedCharacters[forced.Character] = alternatives[RandomNumberGenerator.GetInt32(alternatives.Length)];
        segments[forced.Segment] = segments[forced.Segment] with { Text = new string(forcedCharacters) };
    }

    [GeneratedRegex(@"\[[^\[\]]+\]", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex PlaceholderRegex();

    private sealed record TemplateSegment(string Text, bool IsLiteral);
}
