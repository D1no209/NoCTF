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
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Id,
                item.Mode,
                item.Status,
                item.TrackConfigurationJson,
                item.TrackConfigurationRevision
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
                || !track.IsInternal && (track.IsPublicSelectable || track.VisibleOnLeaderboard)
                || string.Equals(track.Key, viewerTrackKey, StringComparison.OrdinalIgnoreCase))
            .Select(track => Map(track) with
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
            competition.TrackConfigurationRevision,
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
        if (competition.TrackConfigurationRevision != command.ExpectedRevision)
            return Failure(CompetitionTrackFailureCode.ConfigurationConflict,
                "Track configuration revision is stale.");

        var validationErrors = CompetitionTrackPolicy.Validate(competition.Mode, command.Tracks);
        if (validationErrors.Count > 0)
            return Failure(CompetitionTrackFailureCode.InvalidConfiguration,
                string.Join(" ", validationErrors));

        var normalized = CompetitionTrackPolicy.Normalize(command.Tracks);
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
        var currentJson = CompetitionTrackConfiguration.Serialize(
            CompetitionTrackConfiguration.ParseOrDefault(
                competition.Mode,
                competition.TrackConfigurationJson));
        if (string.Equals(nextJson, currentJson, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>.Success(
                Map(competition, normalized));
        }

        competition.TrackConfigurationJson = nextJson;
        competition.TrackConfigurationRevision = checked(competition.TrackConfigurationRevision + 1);
        competition.TrackConfigurationUpdatedAt = command.UpdatedAt;
        competition.LeaderboardDirty = true;
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
            Map(competition, normalized));
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
        if (team.ConcurrencyVersion != command.ExpectedTeamVersion)
            return AssignmentFailure(CompetitionTrackFailureCode.AssignmentConflict,
                "Team revision is stale.");
        if (string.Equals(team.TrackKey, track.Key, StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>.Success(
                new TeamTrackAssignmentView(team.CompetitionId, team.Id, team.TrackKey, team.ConcurrencyVersion));
        }

        var previousTrackKey = team.TrackKey;
        team.TrackKey = track.Key;
        team.ConcurrencyVersion = checked(team.ConcurrencyVersion + 1);
        competition.LeaderboardDirty = true;
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
            new TeamTrackAssignmentView(team.CompetitionId, team.Id, team.TrackKey, team.ConcurrencyVersion));
    }

    private static CompetitionTracksView Map(
        Competition competition,
        CompetitionTrackConfiguration configuration) => new(
        competition.Id,
        competition.Mode,
        competition.Status,
        competition.TrackConfigurationRevision,
        CompetitionTrackPolicy.IsFrozen(competition.Status),
        configuration.Tracks.Select(Map).ToArray());

    private static CompetitionTrackView Map(CompetitionTrackDefinition track) => new(
        track.Key,
        track.Name,
        track.IsDefault,
        track.IsPublicSelectable,
        track.IsInternal,
        track.EarnsScore,
        track.EarnsBlood,
        track.AffectsDynamicChallengeScore,
        track.VisibleOnLeaderboard,
        track.AffectsCompetitiveResults);

    private static OperationResult<CompetitionTracksView, CompetitionTrackFailureCode> Failure(
        CompetitionTrackFailureCode code,
        string message) =>
        OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>.Failure(code, message);

    private static OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode> AssignmentFailure(
        CompetitionTrackFailureCode code,
        string message) =>
        OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>.Failure(code, message);
}
