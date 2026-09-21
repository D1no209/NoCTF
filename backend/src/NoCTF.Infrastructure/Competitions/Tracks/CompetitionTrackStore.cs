using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Common;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams;

namespace NoCTF.Infrastructure.Competitions.Tracks;

public sealed class CompetitionTrackStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events) : ICompetitionTrackStore
{
    private sealed record TrackConfigurationEventPayload(
        int SchemaVersion,
        bool Enabled,
        string DefaultTrackKey,
        IReadOnlyList<string> TrackKeys,
        int ReassignedTeamCount);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<CompetitionTracksView?> GetAsync(
        Guid competitionId,
        Guid? viewerUserId,
        bool includeInternal,
        bool includeInvitationCodes,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Id,
                item.Mode,
                item.Status,
                item.TracksEnabled,
                item.TrackConfigurationJson
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return null;

        var configuration = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
        var hasActiveSsoGates = competition.TracksEnabled
            && configuration.Tracks.Any(track => track.RequiredSsoProviderId is not null);
        IReadOnlyDictionary<Guid, (string Name, string? IconUrl)> providers =
            new Dictionary<Guid, (string Name, string? IconUrl)>();
        if (hasActiveSsoGates)
        {
            var settings = await db.PlatformSettings.AsNoTracking().SingleAsync(cancellationToken);
            providers = settings.SsoConfiguration.Providers.ToDictionary(
                provider => provider.Id,
                provider => (provider.Name, provider.IconUrl));
        }
        Guid? viewerSsoProviderId = null;
        if (hasActiveSsoGates && viewerUserId.HasValue)
        {
            viewerSsoProviderId = await db.Users.AsNoTracking()
                .Where(user => user.Id == viewerUserId.Value)
                .Select(user => user.ExternalIdentityProviderId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        Guid? viewerTeamId = null;
        string? viewerTrackKey = null;
        if (!includeInternal && viewerUserId.HasValue)
        {
            var viewerTeam = await db.Teams.AsNoTracking()
                .Where(team => team.CompetitionId == competitionId
                    && team.DeletedAt == null
                    && team.MemberIds.Contains(viewerUserId.Value))
                .Select(team => new { team.Id, team.TrackKey })
                .SingleOrDefaultAsync(cancellationToken);
            viewerTeamId = viewerTeam?.Id;
            viewerTrackKey = viewerTeam?.TrackKey;
        }

        var tracks = configuration.Tracks
            .Where(track => includeInternal
                || !track.IsInternal
                && (track.IsPublicSelectable
                    || track.VisibleOnLeaderboard
                    || string.Equals(track.Key, viewerTrackKey, StringComparison.OrdinalIgnoreCase)))
            .Select(track => Map(
                track,
                includeInvitationCodes,
                providers,
                viewerSsoProviderId,
                competition.TracksEnabled) with
            {
                IsViewerTrack = string.Equals(
                    track.Key,
                    viewerTrackKey,
                    StringComparison.OrdinalIgnoreCase)
            })
            .ToArray();
        return new CompetitionTracksView(
            competition.Id,
            competition.Mode,
            competition.Status,
            competition.TracksEnabled,
            CompetitionTrackPolicy.CanUpdate(competition.Status),
            tracks,
            viewerTeamId);
    }

    public async Task<UpdateCompetitionTracksResult> UpdateAsync(
        UpdateCompetitionTracksCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db,
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            command.CompetitionId,
            cancellationToken);
        if (competition is null)
            return Failure(CompetitionTrackFailureCode.CompetitionNotFound, "Competition was not found.");
        if (!CompetitionTrackPolicy.CanUpdate(competition.Status))
            return Failure(CompetitionTrackFailureCode.CompetitionFinished,
                "Finished competitions are read-only.");
        var validationErrors = CompetitionTrackPolicy.Validate(competition.Mode, command.Tracks);
        if (validationErrors.Count > 0)
            return Failure(CompetitionTrackFailureCode.InvalidConfiguration,
                string.Join(" ", validationErrors));

        var settings = await db.PlatformSettings.AsNoTracking().SingleAsync(cancellationToken);
        var providers = settings.SsoConfiguration.Providers.ToDictionary(
            provider => provider.Id,
            provider => (provider.Name, provider.IconUrl));
        if (command.Tracks.Any(track =>
                track.RequiredSsoProviderId is Guid providerId
                && !providers.ContainsKey(providerId)))
        {
            return Failure(
                CompetitionTrackFailureCode.SsoProviderNotFound,
                "A track references an SSO provider that does not exist.");
        }

        var currentConfiguration = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
        var normalized = CompetitionTrackPolicy.Normalize(command.Tracks.Select(track =>
        {
            var existingCode = currentConfiguration.Find(track.Key)?.InvitationCode;
            var invitationUpdate = command.InvitationCodeUpdates?.FirstOrDefault(update =>
                string.Equals(update.TrackKey, track.Key, StringComparison.OrdinalIgnoreCase));
            var nextCode = track.IsInternal || !track.IsPublicSelectable
                ? null
                : invitationUpdate?.ClearInvitationCode == true
                    ? null
                    : invitationUpdate?.InvitationCode is not null
                        ? invitationUpdate.InvitationCode
                        : existingCode;
            return track with { InvitationCode = nextCode };
        }).ToArray());
        var nextKeys = normalized.Tracks.Select(track => track.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var teams = await db.Teams
            .Where(team => team.CompetitionId == command.CompetitionId && team.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var reassignmentSources = currentConfiguration.Tracks
            .Select(track => track.Key)
            .Where(key => !nextKeys.Contains(key))
            .Concat(teams.Select(team => team.TrackKey).Where(key => !nextKeys.Contains(key)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reassignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reassignment in command.RemovedTrackReassignments)
        {
            if (string.IsNullOrWhiteSpace(reassignment.FromTrackKey)
                || string.IsNullOrWhiteSpace(reassignment.ToTrackKey)
                || !reassignmentSources.Contains(reassignment.FromTrackKey)
                || !nextKeys.Contains(reassignment.ToTrackKey)
                || string.Equals(
                    reassignment.FromTrackKey,
                    reassignment.ToTrackKey,
                    StringComparison.OrdinalIgnoreCase)
                || !reassignments.TryAdd(
                    reassignment.FromTrackKey,
                    reassignment.ToTrackKey))
            {
                return Failure(
                    CompetitionTrackFailureCode.InvalidTrackReassignment,
                    "Removed track reassignments must uniquely map removed source tracks "
                    + "to tracks that remain in the new configuration.");
            }
        }

        if (command.Enabled)
        {
            var missingSources = reassignmentSources
                .Where(source => teams.Any(team => string.Equals(
                        team.TrackKey,
                        source,
                        StringComparison.OrdinalIgnoreCase))
                    && !reassignments.ContainsKey(source))
                .ToArray();
            if (missingSources.Length > 0)
            {
                var affectedTeamCount = teams.Count(team => missingSources.Contains(
                    team.TrackKey,
                    StringComparer.OrdinalIgnoreCase));
                return Failure(
                    CompetitionTrackFailureCode.TrackReassignmentRequired,
                    "Every removed track assigned to a team requires a reassignment target.",
                    affectedTeamCount);
            }
        }

        var defaultTrackKey = normalized.DefaultTrack.Key;
        var changedTeams = new List<(Team Team, string PreviousTrackKey)>();
        foreach (var team in teams)
        {
            var nextTrackKey = command.Enabled
                ? reassignments.GetValueOrDefault(team.TrackKey) ?? team.TrackKey
                : defaultTrackKey;
            if (!nextKeys.Contains(nextTrackKey))
            {
                return Failure(
                    CompetitionTrackFailureCode.InvalidTrackReassignment,
                    $"Team '{team.Id}' would retain missing track '{nextTrackKey}'.");
            }
            if (string.Equals(team.TrackKey, nextTrackKey, StringComparison.OrdinalIgnoreCase))
                continue;
            changedTeams.Add((team, team.TrackKey));
            team.TrackKey = nextTrackKey;
        }

        var nextJson = CompetitionTrackConfiguration.Serialize(normalized);
        var currentJson = CompetitionTrackConfiguration.Serialize(currentConfiguration);
        var configurationChanged = !string.Equals(nextJson, currentJson, StringComparison.Ordinal);
        var enabledChanged = competition.TracksEnabled != command.Enabled;
        if (!configurationChanged && !enabledChanged && changedTeams.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return UpdateCompetitionTracksResult.Success(
                Map(competition, normalized, includeInvitationCodes: true, providers));
        }

        var affectsLeaderboard = enabledChanged
            || !HasSameLeaderboardShape(currentConfiguration, normalized);
        competition.TracksEnabled = command.Enabled;
        competition.TrackConfigurationJson = nextJson;
        competition.UpdatedAt = command.UpdatedAt;
        foreach (var (team, previousTrackKey) in changedTeams)
        {
            await events.RecordAsync(new CompetitionEventDraft(
                competition.Id,
                CompetitionEventKind.TeamTrackChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Staff,
                command.UpdatedAt,
                ActorUserId: command.ActorUserId,
                TeamId: team.Id,
                TrackKey: team.TrackKey,
                PreviousTrackKey: previousTrackKey), cancellationToken);
        }
        if (configurationChanged || enabledChanged)
        {
            await events.RecordAsync(new CompetitionEventDraft(
                competition.Id,
                affectsLeaderboard
                    ? CompetitionEventKind.TrackConfigurationUpdated
                    : CompetitionEventKind.TrackRegistrationPolicyUpdated,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Staff,
                command.UpdatedAt,
                ActorUserId: command.ActorUserId,
                PayloadJson: JsonSerializer.Serialize(
                    new TrackConfigurationEventPayload(
                        1,
                        command.Enabled,
                        defaultTrackKey,
                        normalized.Tracks.Select(track => track.Key).ToArray(),
                        changedTeams.Count),
                    JsonOptions)), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return UpdateCompetitionTracksResult.Success(
            Map(competition, normalized, includeInvitationCodes: true, providers));
    }

    public async Task<OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
        AssignTeamTrackCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db,
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            command.CompetitionId,
            cancellationToken);
        if (competition is null)
            return AssignmentFailure(CompetitionTrackFailureCode.CompetitionNotFound,
                "Competition was not found.");
        if (!CompetitionTrackPolicy.CanUpdate(competition.Status))
            return AssignmentFailure(
                CompetitionTrackFailureCode.CompetitionFinished,
                "Finished competitions are read-only.");
        if (!competition.TracksEnabled)
            return AssignmentFailure(
                CompetitionTrackFailureCode.TracksDisabled,
                "Team track assignment is unavailable while tracks are disabled.");
        var configuration = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
        var track = configuration.Find(command.TrackKey);
        if (track is null)
            return AssignmentFailure(CompetitionTrackFailureCode.TrackNotFound,
                "Track was not found.");

        var team = await db.Teams.SingleOrDefaultAsync(item =>
            item.Id == command.TeamId
            && item.CompetitionId == command.CompetitionId
            && item.DeletedAt == null,
            cancellationToken);
        if (team is null)
            return AssignmentFailure(CompetitionTrackFailureCode.TeamNotFound, "Team was not found.");
        if (track.RequiredSsoProviderId is Guid requiredProviderId
            && await db.Users.AsNoTracking().CountAsync(user =>
                team.MemberIds.Contains(user.Id)
                && user.ExternalIdentityProviderId == requiredProviderId,
                cancellationToken) != team.MemberIds.Length)
        {
            return AssignmentFailure(
                CompetitionTrackFailureCode.TrackSsoIdentityRequired,
                "Every team member must bind the SSO provider required by this track.");
        }
        if (string.Equals(team.TrackKey, track.Key, StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>.Success(
                new TeamTrackAssignmentView(team.CompetitionId, team.Id, team.TrackKey));
        }

        var previousTrackKey = team.TrackKey;
        team.TrackKey = track.Key;
        await events.RecordAsync(new CompetitionEventDraft(
            competition.Id,
            CompetitionEventKind.TeamTrackChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.UpdatedAt,
            ActorUserId: command.ActorUserId,
            TeamId: team.Id,
            TrackKey: team.TrackKey,
            PreviousTrackKey: previousTrackKey), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>.Success(
            new TeamTrackAssignmentView(team.CompetitionId, team.Id, team.TrackKey));
    }

    private static CompetitionTracksView Map(
        Competition competition,
        CompetitionTrackConfiguration configuration,
        bool includeInvitationCodes,
        IReadOnlyDictionary<Guid, (string Name, string? IconUrl)> providers) => new(
        competition.Id,
        competition.Mode,
        competition.Status,
        competition.TracksEnabled,
        CompetitionTrackPolicy.CanUpdate(competition.Status),
        configuration.Tracks.Select(track => Map(
            track,
            includeInvitationCodes,
            providers,
            viewerSsoProviderId: null,
            competition.TracksEnabled)).ToArray());

    private static bool HasSameLeaderboardShape(
        CompetitionTrackConfiguration current,
        CompetitionTrackConfiguration next) =>
        current.Tracks.Select(ToLeaderboardShape)
            .SequenceEqual(next.Tracks.Select(ToLeaderboardShape));

    private static object ToLeaderboardShape(CompetitionTrackDefinition track) => new
    {
        track.Key,
        track.Name,
        track.IsDefault,
        track.IsInternal,
        track.EarnsScore,
        track.EarnsBlood,
        track.AffectsDynamicChallengeScore,
        track.VisibleOnLeaderboard,
        track.AffectsCompetitiveResults
    };

    private static CompetitionTrackView Map(
        CompetitionTrackDefinition track,
        bool includeInvitationCode,
        IReadOnlyDictionary<Guid, (string Name, string? IconUrl)> providers,
        Guid? viewerSsoProviderId,
        bool ssoGatesEnabled)
    {
        (string Name, string? IconUrl)? provider = null;
        if (track.RequiredSsoProviderId is Guid providerId
            && providers.TryGetValue(providerId, out var configuredProvider))
            provider = configuredProvider;
        return new(
        track.Key,
        track.Name,
        track.IsDefault,
        track.IsPublicSelectable,
        track.IsInternal,
        track.EarnsScore,
        track.EarnsBlood,
        track.AffectsDynamicChallengeScore,
        track.VisibleOnLeaderboard,
        track.AffectsCompetitiveResults,
        IsViewerTrack: false,
        track.RequiresInvitationCode,
        includeInvitationCode ? track.InvitationCode : null,
        track.RequiredSsoProviderId,
        provider?.Name,
        provider?.IconUrl,
        !ssoGatesEnabled
            || track.RequiredSsoProviderId is null
            || track.RequiredSsoProviderId == viewerSsoProviderId);
    }

    private static UpdateCompetitionTracksResult Failure(
        CompetitionTrackFailureCode code,
        string message,
        int affectedTeamCount = 0) =>
        UpdateCompetitionTracksResult.Failure(code, message, affectedTeamCount);

    private static OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode> AssignmentFailure(
        CompetitionTrackFailureCode code,
        string message) =>
        OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>.Failure(code, message);
}
