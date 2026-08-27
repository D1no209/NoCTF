using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Common;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams;

namespace NoCTF.Infrastructure.Competitions.Tracks;

public sealed class CompetitionTrackStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events) : ICompetitionTrackStore
{
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
                item.TrackConfigurationJson
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return null;

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

        var configuration = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
        var tracks = configuration.Tracks
            .Where(track => includeInternal
                || !track.IsInternal
                && (track.IsPublicSelectable
                    || track.VisibleOnLeaderboard
                    || string.Equals(track.Key, viewerTrackKey, StringComparison.OrdinalIgnoreCase)))
            .Select(track => Map(track, includeInvitationCodes) with
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
            CompetitionTrackPolicy.IsFrozen(competition.Status),
            tracks,
            viewerTeamId);
    }

    public async Task<OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>> UpdateAsync(
        UpdateCompetitionTracksCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            command.CompetitionId,
            cancellationToken);
        if (competition is null)
            return Failure(CompetitionTrackFailureCode.CompetitionNotFound, "Competition was not found.");
        if (CompetitionTrackPolicy.IsFrozen(competition.Status))
            return Failure(CompetitionTrackFailureCode.ConfigurationLocked,
                "Track definitions are frozen after the competition first starts.");
        var validationErrors = CompetitionTrackPolicy.Validate(competition.Mode, command.Tracks);
        if (validationErrors.Count > 0)
            return Failure(CompetitionTrackFailureCode.InvalidConfiguration,
                string.Join(" ", validationErrors));

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
        var usedKeys = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == command.CompetitionId && team.DeletedAt == null)
            .Select(team => team.TrackKey)
            .Distinct()
            .ToListAsync(cancellationToken);
        var missingUsedKey = usedKeys.FirstOrDefault(key => !nextKeys.Contains(key));
        if (missingUsedKey is not null)
            return Failure(CompetitionTrackFailureCode.TrackInUse,
                $"Track '{missingUsedKey}' is still assigned to one or more teams.");

        var nextJson = CompetitionTrackConfiguration.Serialize(normalized);
        var currentJson = CompetitionTrackConfiguration.Serialize(currentConfiguration);
        if (string.Equals(nextJson, currentJson, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>.Success(
                Map(competition, normalized, includeInvitationCodes: true));
        }

        competition.TrackConfigurationJson = nextJson;
        competition.UpdatedAt = command.UpdatedAt;
        await events.RecordAsync(new CompetitionEventDraft(
            competition.Id,
            CompetitionEventKind.TrackConfigurationUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.UpdatedAt,
            ActorUserId: command.ActorUserId), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>.Success(
            Map(competition, normalized, includeInvitationCodes: true));
    }

    public async Task<OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
        AssignTeamTrackCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            command.CompetitionId,
            cancellationToken);
        if (competition is null)
            return AssignmentFailure(CompetitionTrackFailureCode.CompetitionNotFound,
                "Competition was not found.");
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
        bool includeInvitationCodes) => new(
        competition.Id,
        competition.Mode,
        competition.Status,
        CompetitionTrackPolicy.IsFrozen(competition.Status),
        configuration.Tracks.Select(track => Map(track, includeInvitationCodes)).ToArray());

    private static CompetitionTrackView Map(
        CompetitionTrackDefinition track,
        bool includeInvitationCode) => new(
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
        includeInvitationCode ? track.InvitationCode : null);

    private static OperationResult<CompetitionTracksView, CompetitionTrackFailureCode> Failure(
        CompetitionTrackFailureCode code,
        string message) =>
        OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>.Failure(code, message);

    private static OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode> AssignmentFailure(
        CompetitionTrackFailureCode code,
        string message) =>
        OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>.Failure(code, message);
}
