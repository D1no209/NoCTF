using System.Text.RegularExpressions;
using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Tracks;

public sealed record CompetitionTrackView(
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
    bool IsViewerTrack = false,
    bool RequiresInvitationCode = false,
    string? InvitationCode = null);

public sealed record CompetitionTracksView(
    Guid CompetitionId,
    GameMode Mode,
    CompetitionStatus Status,
    bool Enabled,
    bool CanUpdate,
    IReadOnlyList<CompetitionTrackView> Tracks,
    Guid? ViewerTeamId = null);

public enum CompetitionTrackFailureCode
{
    CompetitionNotFound,
    InvalidConfiguration,
    CompetitionFinished,
    TracksDisabled,
    TrackReassignmentRequired,
    InvalidTrackReassignment,
    TeamNotFound,
    TrackNotFound,
    TrackNotPublicSelectable
}

public sealed record CompetitionTrackInvitationCodeUpdate(
    string TrackKey,
    string? InvitationCode,
    bool ClearInvitationCode);

public sealed record RemovedTrackReassignment(
    string FromTrackKey,
    string ToTrackKey);

public sealed record UpdateCompetitionTracksCommand(
    Guid CompetitionId,
    bool Enabled,
    IReadOnlyList<CompetitionTrackDefinition> Tracks,
    IReadOnlyList<RemovedTrackReassignment> RemovedTrackReassignments,
    Guid ActorUserId,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CompetitionTrackInvitationCodeUpdate>? InvitationCodeUpdates = null);

public sealed record UpdateCompetitionTracksResult(
    CompetitionTracksView? Value,
    CompetitionTrackFailureCode? FailureCode = null,
    string? ErrorMessage = null,
    int AffectedTeamCount = 0)
{
    public bool Succeeded => Value is not null && FailureCode is null;

    public static UpdateCompetitionTracksResult Success(CompetitionTracksView value) =>
        new(value);

    public static UpdateCompetitionTracksResult Failure(
        CompetitionTrackFailureCode code,
        string message,
        int affectedTeamCount = 0) =>
        new(null, code, message, affectedTeamCount);
}

public sealed record AssignTeamTrackCommand(
    Guid CompetitionId,
    Guid TeamId,
    string TrackKey,
    Guid ActorUserId,
    DateTimeOffset UpdatedAt);

public sealed record TeamTrackAssignmentView(
    Guid CompetitionId,
    Guid TeamId,
    string TrackKey);

public interface ICompetitionTrackStore
{
    Task<CompetitionTracksView?> GetAsync(
        Guid competitionId,
        Guid? viewerUserId,
        bool includeInternal,
        bool includeInvitationCodes,
        CancellationToken cancellationToken);

    Task<UpdateCompetitionTracksResult> UpdateAsync(
        UpdateCompetitionTracksCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
        AssignTeamTrackCommand command,
        CancellationToken cancellationToken);
}

public static partial class CompetitionTrackPolicy
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public static IReadOnlyList<string> Validate(
        GameMode mode,
        IReadOnlyList<CompetitionTrackDefinition> tracks)
    {
        var errors = new List<string>();
        if (tracks.Count is < 1 or > 32)
            errors.Add("A competition must define between 1 and 32 tracks.");
        if (tracks.Count(track => track.IsDefault) != 1)
            errors.Add("Exactly one default track is required.");
        var defaultTrack = tracks.FirstOrDefault(track => track.IsDefault);
        if (defaultTrack is not null
            && (defaultTrack.IsInternal || !defaultTrack.IsPublicSelectable))
        {
            errors.Add("The default track must be publicly selectable and cannot be internal.");
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracks)
        {
            var key = CompetitionTrackConfiguration.NormalizeKey(track.Key);
            if (key is null || !KeyPattern().IsMatch(key))
                errors.Add("Track keys must be 1-64 lowercase letters, digits, dots, underscores or hyphens.");
            else if (!keys.Add(key))
                errors.Add($"Track key '{key}' is duplicated.");
            if (string.IsNullOrWhiteSpace(track.Name) || track.Name.Trim().Length > 80)
                errors.Add($"Track '{key ?? track.Key}' must have a name of at most 80 characters.");
            if (track.IsInternal && (track.IsPublicSelectable
                    || track.EarnsScore
                    || track.EarnsBlood
                    || track.AffectsDynamicChallengeScore
                    || track.VisibleOnLeaderboard
                    || track.AffectsCompetitiveResults))
                errors.Add($"Internal track '{key ?? track.Key}' cannot affect public competition results.");
            if (track.EarnsBlood && (!track.EarnsScore || mode != GameMode.Ctf))
                errors.Add($"Track '{key ?? track.Key}' can earn blood only in CTF while earning score.");
            if (mode != GameMode.Ctf && track.AffectsDynamicChallengeScore)
                errors.Add($"Track '{key ?? track.Key}' can affect dynamic challenge scores only in CTF.");
        }
        return errors.Distinct().ToArray();
    }

    public static IReadOnlyList<string> Validate(
        GameMode mode,
        CompetitionTrackConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.SchemaVersion != CompetitionTrackConfiguration.CurrentSchemaVersion)
            errors.Add($"Track configuration schema version must be {CompetitionTrackConfiguration.CurrentSchemaVersion}.");
        errors.AddRange(Validate(mode, configuration.Tracks));
        return errors.Distinct().ToArray();
    }

    public static CompetitionTrackConfiguration Normalize(
        IReadOnlyList<CompetitionTrackDefinition> tracks) => new(
        CompetitionTrackConfiguration.CurrentSchemaVersion,
        tracks.Select(track => track with
        {
            Key = CompetitionTrackConfiguration.NormalizeKey(track.Key) ?? string.Empty,
            Name = track.Name.Trim()
        }).ToArray());

    public static bool CanUpdate(CompetitionStatus status) =>
        status != CompetitionStatus.Finished;
}

public sealed class GetCompetitionTracks(ICompetitionTrackStore store)
{
    public Task<CompetitionTracksView?> ExecuteAsync(
        Guid competitionId,
        Guid? viewerUserId,
        bool includeInternal,
        bool includeInvitationCodes,
        CancellationToken cancellationToken = default) =>
        store.GetAsync(
            competitionId,
            viewerUserId,
            includeInternal,
            includeInvitationCodes,
            cancellationToken);
}

public sealed class UpdateCompetitionTracks(ICompetitionTrackStore store)
{
    public async Task<UpdateCompetitionTracksResult> ExecuteAsync(
        UpdateCompetitionTracksCommand command,
        GameMode mode,
        CancellationToken cancellationToken = default)
    {
        var errors = CompetitionTrackPolicy.Validate(mode, command.Tracks);
        foreach (var update in command.InvitationCodeUpdates ?? [])
        {
            if (!string.IsNullOrWhiteSpace(update.InvitationCode)
                && update.InvitationCode.Trim().Length is < 8 or > 128)
            {
                errors = errors.Append(
                    $"Track '{update.TrackKey}' invitation code must be between 8 and 128 characters.")
                    .ToArray();
            }
        }
        if (errors.Count > 0)
            return UpdateCompetitionTracksResult.Failure(
                CompetitionTrackFailureCode.InvalidConfiguration,
                string.Join(" ", errors));
        return await store.UpdateAsync(command with
        {
            Tracks = CompetitionTrackPolicy.Normalize(command.Tracks).Tracks,
            InvitationCodeUpdates = command.InvitationCodeUpdates?.Select(update => update with
            {
                TrackKey = CompetitionTrackConfiguration.NormalizeKey(update.TrackKey) ?? string.Empty,
                InvitationCode = string.IsNullOrWhiteSpace(update.InvitationCode)
                    ? null
                    : update.InvitationCode.Trim()
            }).ToArray(),
            RemovedTrackReassignments = command.RemovedTrackReassignments.Select(reassignment =>
                reassignment with
                {
                    FromTrackKey = CompetitionTrackConfiguration.NormalizeKey(
                        reassignment.FromTrackKey) ?? string.Empty,
                    ToTrackKey = CompetitionTrackConfiguration.NormalizeKey(
                        reassignment.ToTrackKey) ?? string.Empty
                }).ToArray()
        }, cancellationToken);
    }
}

public sealed class AssignTeamTrack(ICompetitionTrackStore store)
{
    public Task<OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> ExecuteAsync(
        AssignTeamTrackCommand command,
        CancellationToken cancellationToken = default) =>
        store.AssignAsync(command with
        {
            TrackKey = CompetitionTrackConfiguration.NormalizeKey(command.TrackKey) ?? string.Empty
        }, cancellationToken);
}
