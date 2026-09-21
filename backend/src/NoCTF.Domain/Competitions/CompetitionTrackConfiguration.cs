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
    string? InvitationCode = null,
    Guid? RequiredSsoProviderId = null)
{
    [JsonIgnore]
    public bool RequiresInvitationCode => !string.IsNullOrWhiteSpace(InvitationCode);
}

public static class CompetitionTrackInvitationCode
{
    public static bool Verify(string? invitationCode, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(invitationCode) || string.IsNullOrWhiteSpace(candidate))
            return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(invitationCode.Trim()));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Trim()));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
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

    public static CompetitionTrackConfiguration EffectiveFor(
        GameMode mode,
        bool tracksEnabled,
        string? json)
    {
        var saved = ParseOrDefault(mode, json);
        if (tracksEnabled)
            return saved;
        var defaultTrack = saved.DefaultTrack;
        return new(
            CurrentSchemaVersion,
            [defaultTrack with
            {
                IsDefault = true,
                IsPublicSelectable = true,
                IsInternal = false,
                EarnsScore = true,
                EarnsBlood = mode == GameMode.Ctf,
                AffectsDynamicChallengeScore = mode == GameMode.Ctf,
                VisibleOnLeaderboard = true,
                AffectsCompetitiveResults = true,
                InvitationCode = null,
                RequiredSsoProviderId = null
            }]);
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
