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
    public Guid CompetitionId { get; init; }
    public int Position { get; init; }

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
    IReadOnlyList<CompetitionTrackDefinition> Tracks)
{
    public const string DefaultTrackKey = "default";
    public static CompetitionTrackConfiguration DefaultFor(GameMode mode) => new(
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

    public static CompetitionTrackConfiguration FromPersisted(
        GameMode mode,
        IReadOnlyList<CompetitionTrackDefinition>? tracks)
    {
        if (tracks is null or { Count: 0 })
            throw new InvalidOperationException(
                "Persisted track configuration must contain at least one track.");
        if (tracks.Count(track => track.IsDefault) != 1)
            throw new InvalidOperationException(
                "Persisted track configuration must contain exactly one default track.");
        return new(tracks.OrderBy(track => track.Position).ToArray());
    }

    public static CompetitionTrackConfiguration EffectiveFor(
        GameMode mode,
        bool tracksEnabled,
        IReadOnlyList<CompetitionTrackDefinition>? tracks)
    {
        if (tracksEnabled)
            return FromPersisted(mode, tracks);
        var defaultTrack = tracks is { Count: > 0 }
            ? FromPersisted(mode, tracks).DefaultTrack
            : DefaultFor(mode).DefaultTrack;
        return new(
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

    public static List<CompetitionTrackDefinition> ToPersisted(
        CompetitionTrackConfiguration configuration,
        Guid competitionId = default) => configuration.Tracks
        .Select((track, position) => track with
        {
            CompetitionId = competitionId,
            Position = position
        })
        .ToList();

    public static string? NormalizeKey(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
