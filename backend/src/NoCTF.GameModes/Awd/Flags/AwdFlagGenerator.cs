using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NoCTF.GameModes.Flags;

namespace NoCTF.GameModes.Awd.Flags;

public readonly record struct AwdFlagIdentity(
    Guid CompetitionId, Guid CompetitionChallengeId, Guid TeamId, Guid RoundId,
    Guid StoreId, long GenerationSlot, int Ordinal, int KeyVersion = 1);

/// <summary>AWD-only, retry-stable derivation. Other modes retain their existing generator.</summary>
public static partial class AwdFlagGenerator
{
    public static PerTeamFlagTemplate Default { get; } = new("flag", "[SECURE]", false);

    public static string Generate(PerTeamFlagTemplate template, ReadOnlySpan<byte> key, AwdFlagIdentity identity)
    {
        if (key.Length != 32) throw new ArgumentException("AWD derivation requires a 32-byte key.", nameof(key));
        if (identity.CompetitionId == Guid.Empty || identity.CompetitionChallengeId == Guid.Empty
            || identity.TeamId == Guid.Empty || identity.RoundId == Guid.Empty || identity.StoreId == Guid.Empty
            || identity.GenerationSlot < 0 || identity.Ordinal < 0 || identity.KeyVersion <= 0)
            throw new ArgumentException("AWD flag identity must name a real, nonnegative issuance slot.", nameof(identity));
        if (!IsValidTemplate(template)) throw new FormatException("AWD template must preserve at least 128 unpredictable bits.");
        var domain = "NoCTF/AWD/flag/v1"u8;
        Span<byte> input = stackalloc byte[domain.Length + 5 * 16 + 8 + 4 + 4];
        domain.CopyTo(input);
        var offset = domain.Length;
        foreach (var id in new[] { identity.CompetitionId, identity.CompetitionChallengeId, identity.TeamId, identity.RoundId, identity.StoreId })
        {
            id.TryWriteBytes(input.Slice(offset, 16), bigEndian: true, out _);
            offset += 16;
        }
        BinaryPrimitives.WriteInt64BigEndian(input.Slice(offset, 8), identity.GenerationSlot);
        BinaryPrimitives.WriteInt32BigEndian(input.Slice(offset + 8, 4), identity.Ordinal);
        BinaryPrimitives.WriteInt32BigEndian(input.Slice(offset + 12, 4), identity.KeyVersion);
        var digest = HMACSHA256.HashData(key, input);
        var body = TokenRegex().Replace(template.BodyTemplate, match =>
        {
            var token = match.Groups[1].Value;
            if (token.StartsWith("SECURE", StringComparison.Ordinal))
            {
                var length = token.Length == 6 ? 24 : int.Parse(token[7..], CultureInfo.InvariantCulture);
                return Convert.ToBase64String(digest.AsSpan(0, length)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
            return token switch
            {
                "TEAMID" => identity.TeamId.ToString("N"),
                "COMPETITIONID" => identity.CompetitionId.ToString("N"),
                "COMPETITIONCHALLENGEID" => identity.CompetitionChallengeId.ToString("N"),
                "STOREID" => identity.StoreId.ToString("N"),
                "ROUNDID" => identity.RoundId.ToString("N"),
                "ORDINAL" => identity.Ordinal.ToString(CultureInfo.InvariantCulture),
                _ => throw new FormatException("Unknown AWD flag placeholder.")
            };
        });
        return template.Header.Length == 0 ? body : $"{template.Header}{{{body}}}";
    }

    public static bool IsValidTemplate(PerTeamFlagTemplate? template)
    {
        if (template is null || template.LeetLiteralText || template.Header is null || template.BodyTemplate is null
            || template.Header.Length > 64 || template.BodyTemplate.Length > 512
            || (template.Header + template.BodyTemplate).Any(character => character is < '!' or > '~')
            || template.Header.Contains('[') || template.Header.Contains(']')) return false;
        var secure = false;
        foreach (Match match in TokenRegex().Matches(template.BodyTemplate))
        {
            var token = match.Groups[1].Value;
            if (token == "SECURE") secure = true;
            else if (token.StartsWith("SECURE:", StringComparison.Ordinal))
            {
                if (!int.TryParse(token.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes)
                    || bytes is < 16 or > 32) return false;
                secure = true;
            }
            else if (token is not ("TEAMID" or "COMPETITIONID" or "COMPETITIONCHALLENGEID" or "STOREID" or "ROUNDID" or "ORDINAL"))
                return false;
        }
        var literals = TokenRegex().Replace(template.BodyTemplate, string.Empty);
        return secure && !literals.Contains('[') && !literals.Contains(']');
    }

    [GeneratedRegex(@"\[([^\[\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
