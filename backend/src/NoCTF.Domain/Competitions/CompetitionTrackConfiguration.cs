using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Domain.Competitions;

public sealed record CompetitionTrackDefinition(
    string Key,
    string Name,
    bool IsDefault,
    bool IsPublicSelectable,
    bool IsInternal,
    bool EarnsScore,
    bool EarnsBlood,
    bool AffectsDynamicChallengeScore,
    bool VisibleOnLeaderboard,
    bool AffectsCompetitiveResults,
    string? InvitationCodeHash = null)
{
    [JsonIgnore]
    public bool RequiresInvitationCode => !string.IsNullOrWhiteSpace(InvitationCodeHash);
}

public static class CompetitionTrackInvitationCode
{
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const int Iterations = 100_000;

    public static string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(value.Trim()),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashLength);
        return $"v1${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string? storedHash, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(candidate))
            return false;
        var parts = storedHash.Split('$', StringSplitOptions.None);
        if (parts is not ["v1", _, _])
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            if (salt.Length != SaltLength || expected.Length != HashLength)
                return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(candidate.Trim()),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashLength);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record CompetitionTrackConfiguration(
    int SchemaVersion,
    IReadOnlyList<CompetitionTrackDefinition> Tracks)
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultTrackKey = "default";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CompetitionTrackConfiguration DefaultFor(GameMode mode) => new(
        CurrentSchemaVersion,
        [new(
            DefaultTrackKey,
            "Default",
            IsDefault: true,
            IsPublicSelectable: true,
            IsInternal: false,
            EarnsScore: true,
            EarnsBlood: mode == GameMode.Ctf,
            AffectsDynamicChallengeScore: mode == GameMode.Ctf,
            VisibleOnLeaderboard: true,
            AffectsCompetitiveResults: true)]);

    [JsonIgnore]
    public CompetitionTrackDefinition DefaultTrack =>
        Tracks.Single(track => track.IsDefault);

    public CompetitionTrackDefinition? Find(string? key)
    {
        var normalized = NormalizeKey(key);
        return normalized is null
            ? null
            : Tracks.FirstOrDefault(track =>
                string.Equals(track.Key, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static CompetitionTrackConfiguration ParseOrDefault(GameMode mode, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return DefaultFor(mode);
        return TryParse(json, out var configuration)
            && configuration.SchemaVersion == CurrentSchemaVersion
            && configuration.Tracks is { Count: > 0 }
            && configuration.Tracks.Count(track => track.IsDefault) == 1
            ? configuration
            : DefaultFor(mode);
    }

    public static bool TryParse(
        string json,
        out CompetitionTrackConfiguration configuration)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<CompetitionTrackConfiguration>(json, JsonOptions);
            if (parsed?.Tracks is null)
            {
                configuration = DefaultFor(GameMode.Ctf);
                return false;
            }

            configuration = parsed;
            return true;
        }
        catch (JsonException)
        {
            configuration = DefaultFor(GameMode.Ctf);
            return false;
        }
    }

    public static string Serialize(CompetitionTrackConfiguration configuration) =>
        JsonSerializer.Serialize(configuration, JsonOptions);

    public static string? NormalizeKey(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
