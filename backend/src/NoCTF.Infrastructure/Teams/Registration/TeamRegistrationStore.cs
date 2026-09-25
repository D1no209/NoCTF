using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Teams;

namespace NoCTF.Infrastructure.Teams.Registration;

public sealed class TeamRegistrationStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    TimeProvider? clock = null,
    ICompetitionEventRecorder? eventRecorder = null) : ITeamRegistrationStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public Task<TeamRegistrationPolicy?> GetPolicyAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().Where(x => x.Id == competitionId)
            .Select(x => new TeamRegistrationPolicy(
                x.Status,
                x.TeamRegistrationAutoApprove,
                x.DeletedAt != null,
                x.AllowTeamRegistrationWhileRunning,
                x.Mode,
                x.Tracks,
                x.TracksEnabled,
                x.PracticeModeEnabled))
            .SingleOrDefaultAsync(ct);

    public async Task<TeamCreateStoreResult> TryCreateAsync(
        CreateTeamCommand command,
        TeamRegistrationStatus status,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await TryCreateOnceAsync(command, status, ct);
            }
            catch (Exception exception) when (attempt < 2
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
        return new(null, TeamRegistrationFailure.TeamNameOrMembershipConflict);
    }

    private async Task<TeamCreateStoreResult> TryCreateOnceAsync(
        CreateTeamCommand command,
        TeamRegistrationStatus status,
        CancellationToken ct)
    {
        var name = command.Name.Trim();
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db,
            System.Data.IsolationLevel.Serializable,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            command.CompetitionId,
            ct);
        if (competition is null) return new(null, TeamRegistrationFailure.CompetitionNotFound);
        var practice = competition.Mode == GameMode.Ctf && competition.Status == CompetitionStatus.Finished && competition.PracticeModeEnabled;
        if (!practice && (competition.Status is CompetitionStatus.Paused or CompetitionStatus.Finished
            || competition.Status == CompetitionStatus.Running
                && !competition.AllowTeamRegistrationWhileRunning))
            return new(null, TeamRegistrationFailure.RegistrationClosed);
        var tracks = CompetitionTrackConfiguration.FromPersisted(
            competition.Mode,
            competition.Tracks);
        var track = tracks.DefaultTrack;
        if (competition.TracksEnabled)
        {
            var requestedTrackKey = CompetitionTrackConfiguration.NormalizeKey(command.TrackKey);
            if (requestedTrackKey is null)
                return new(null, TeamRegistrationFailure.TrackNotFound);
            track = tracks.Find(requestedTrackKey);
            if (track is null)
                return new(null, TeamRegistrationFailure.TrackNotFound);
            if (track.IsInternal || !track.IsPublicSelectable)
                return new(null, TeamRegistrationFailure.TrackNotPublicSelectable);
        }
        var userIdentity = await db.Users.AsNoTracking()
            .Where(user => user.Id == command.UserId)
            .Select(user => new
            {
                user.Id,
                ExternalIdentityProviderId = user.ExternalIdentity == null
                    ? (Guid?)null
                    : user.ExternalIdentity.ProviderId
            })
            .SingleOrDefaultAsync(ct);
        if (userIdentity is null)
            return new(null, TeamRegistrationFailure.TeamNameOrMembershipConflict);
        if (await db.Teams.AnyAsync(x => x.CompetitionId == command.CompetitionId
            && x.DeletedAt == null
            && x.Members.Any(member => member.UserId == command.UserId), ct))
            return new(null, TeamRegistrationFailure.UserAlreadyRegistered);
        var id = Guid.CreateVersion7(command.RegisteredAt);
        var team = new Team
        {
            Id = id,
            CompetitionId = command.CompetitionId,
            TrackKey = track.Key,
            Name = name,
            CaptainId = command.UserId,
            MemberIds = [command.UserId],
            InvitationToken = CreateInvitationToken(),
            RegistrationStatus = practice ? TeamRegistrationStatus.Approved : status,
            RegisteredAt = command.RegisteredAt
        };
        db.Teams.Add(team);
        await events.RecordAsync(new(
            team.CompetitionId,
            CompetitionEventKind.TeamRegistered,
            CompetitionEventLevel.Information,
            !practice && status == TeamRegistrationStatus.Approved && !track.IsInternal
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Staff,
            command.RegisteredAt,
            ActorUserId: command.UserId,
            RelatedUserId: command.UserId,
            TeamId: team.Id,
            TeamRegistrationStatus: team.RegistrationStatus,
            TrackKey: track.Key), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (await db.Set<TeamMember>().AsNoTracking().AnyAsync(member =>
                    member.CompetitionId == command.CompetitionId
                    && member.UserId == command.UserId,
                    ct))
                return new(null, TeamRegistrationFailure.UserAlreadyRegistered);
            return new(null, TeamRegistrationFailure.TeamNameOrMembershipConflict);
        }
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return new(Map(team), null);
    }

    public Task<IReadOnlyList<TeamView>> ListAsync(
        Guid competitionId,
        bool includePending,
        CancellationToken ct) =>
        ListCoreAsync(competitionId, includePending, includeInternal: true, ct);

    public Task<IReadOnlyList<TeamView>> ListPublicAsync(
        Guid competitionId,
        bool includePending,
        CancellationToken ct) =>
        ListCoreAsync(competitionId, includePending, includeInternal: false, ct);

    public Task<TeamListPage> ListPageAsync(TeamListQuery query, CancellationToken ct) =>
        ListPageCoreAsync(query, ct);

    private async Task<TeamListPage> ListPageCoreAsync(
        TeamListQuery query,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == query.CompetitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Mode,
                item.TracksEnabled,
                item.Tracks
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return new([], 0);

        var configuration = CompetitionTrackConfiguration.EffectiveFor(
            competition.Mode,
            competition.TracksEnabled,
            competition.Tracks);
        var internalKeys = configuration.Tracks.Where(track => track.IsInternal)
            .Select(track => track.Key)
            .ToArray();
        var source = db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == query.CompetitionId
                && team.DeletedAt == null
                && (query.IncludePending || team.RegistrationStatus == TeamRegistrationStatus.Approved)
                && (query.IncludeInternal || !internalKeys.Contains(team.TrackKey)));
        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();
        if (keyword is not null)
        {
            var normalized = keyword.ToUpperInvariant();
            source = source.Where(team => team.NormalizedName.Contains(normalized));
        }

        var total = await source.CountAsync(ct);
        var ordered = query.Desc
            ? source.OrderByDescending(team => team.Name).ThenByDescending(team => team.Id)
            : source.OrderBy(team => team.Name).ThenBy(team => team.Id);
        var teams = await ordered
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToListAsync(ct);
        return new(teams.Select(team => Map(team, configuration)).ToArray(), total);
    }

    private async Task<IReadOnlyList<TeamView>> ListCoreAsync(
        Guid competitionId,
        bool includePending,
        bool includeInternal,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Mode,
                item.TracksEnabled,
                item.Tracks
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return [];
        var configuration = CompetitionTrackConfiguration.EffectiveFor(
            competition.Mode,
            competition.TracksEnabled,
            competition.Tracks);
        var internalKeys = configuration.Tracks.Where(track => track.IsInternal)
            .Select(track => track.Key)
            .ToArray();
        var teams = await db.Teams.AsNoTracking()
            .Where(x => x.CompetitionId == competitionId && x.DeletedAt == null
                && (includePending || x.RegistrationStatus == TeamRegistrationStatus.Approved)
                && (includeInternal || !internalKeys.Contains(x.TrackKey)))
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        return teams.Select(team => Map(team, configuration)).ToArray();
    }

    public async Task<TeamReviewStoreResult> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Status,
                item.Mode,
                item.TracksEnabled,
                item.Tracks
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null) return new(false, TeamRegistrationFailure.CompetitionNotFound);
        if (competition.Status == CompetitionStatus.Finished) return new(false, TeamRegistrationFailure.CompetitionFinished);
        var team = await db.Teams.AsNoTracking().Where(item => item.Id == teamId
                && item.CompetitionId == competitionId && item.DeletedAt == null)
            .Select(item => new { item.TrackKey })
            .SingleOrDefaultAsync(ct);
        if (team is null) return new(false, TeamRegistrationFailure.TeamNotFound);
        var track = CompetitionTrackConfiguration.EffectiveFor(
            competition.Mode,
            competition.TracksEnabled,
            competition.Tracks).Find(team.TrackKey);
        var changed = await db.Teams.Where(x => x.Id == teamId && x.CompetitionId == competitionId
                && x.RegistrationStatus != status)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RegistrationStatus, status), ct);
        if (changed == 1)
        {
            await events.RecordAsync(new(
                competitionId,
                CompetitionEventKind.TeamRegistrationChanged,
                status == TeamRegistrationStatus.Rejected
                    ? CompetitionEventLevel.Warning
                    : CompetitionEventLevel.Information,
                status == TeamRegistrationStatus.Approved && track?.IsInternal != true
                    ? CompetitionEventVisibility.Public
                    : CompetitionEventVisibility.Staff,
                timeProvider.GetUtcNow(),
                TeamId: teamId,
                TeamRegistrationStatus: status,
                TrackKey: team.TrackKey), ct);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return changed == 1 ? new(true) : new(false, TeamRegistrationFailure.TeamReviewConflict);
    }

    public async Task<TeamReviewStoreResult> SubmitAsync(
        Guid competitionId,
        Guid teamId,
        Guid userId,
        string? trackInvitationCode,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(
            db,
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return new(false, TeamRegistrationFailure.CompetitionNotFound);
        if (!ParticipantTeamMutationPolicy.CanChangeOrganization(
                competition.Status,
                competition.AllowTeamRegistrationWhileRunning))
            return new(false, TeamRegistrationFailure.RegistrationClosed);
        var team = await db.Teams.SingleOrDefaultAsync(item =>
            item.Id == teamId
            && item.CompetitionId == competitionId
            && item.CaptainId == userId
            && item.DeletedAt == null
            && (item.RegistrationStatus == TeamRegistrationStatus.Unregistered
                || item.RegistrationStatus == TeamRegistrationStatus.Rejected), ct);
        if (team is null)
            return new(false, TeamRegistrationFailure.TeamReviewConflict);
        if (team.IsBanned)
            return new(false, TeamRegistrationFailure.TeamBanned);
        if (team.IsLocked)
            return new(false, TeamRegistrationFailure.TeamLocked);
        var configuration = CompetitionTrackConfiguration.FromPersisted(
            competition.Mode,
            competition.Tracks);
        var track = configuration.Find(team.TrackKey);
        if (track is null)
            return new(false, TeamRegistrationFailure.TrackNotFound);
        if (competition.TracksEnabled && (track.IsInternal || !track.IsPublicSelectable))
            return new(false, TeamRegistrationFailure.TrackNotPublicSelectable);
        if (competition.TracksEnabled && track.RequiresInvitationCode
            && string.IsNullOrWhiteSpace(trackInvitationCode))
            return new(false, TeamRegistrationFailure.TrackInvitationRequired);
        if (competition.TracksEnabled && track.RequiresInvitationCode
            && !CompetitionTrackInvitationCode.Verify(track.InvitationCode, trackInvitationCode))
            return new(false, TeamRegistrationFailure.TrackInvitationInvalid);
        if (competition.TracksEnabled
            && track.RequiredSsoProviderId is Guid requiredProviderId
            )
        {
            var memberIds = await db.Set<TeamMember>().AsNoTracking()
                .Where(member => member.TeamId == team.Id)
                .Select(member => member.UserId)
                .ToArrayAsync(ct);
            var eligibleMemberCount = await db.Set<ExternalIdentity>().AsNoTracking()
                .CountAsync(identity => memberIds.Contains(identity.UserId)
                    && identity.ProviderId == requiredProviderId, ct);
            if (eligibleMemberCount != memberIds.Length)
                return new(false, TeamRegistrationFailure.TrackSsoIdentityRequired);
        }

        var nextStatus = competition.TeamRegistrationAutoApprove
            ? TeamRegistrationStatus.Approved
            : TeamRegistrationStatus.Pending;
        team.RegistrationStatus = nextStatus;
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.TeamRegistrationChanged,
            CompetitionEventLevel.Information,
            nextStatus == TeamRegistrationStatus.Approved && !track.IsInternal
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Staff,
            timeProvider.GetUtcNow(),
            ActorUserId: userId,
            RelatedUserId: userId,
            TeamId: teamId,
            TeamRegistrationStatus: nextStatus,
            TrackKey: team.TrackKey), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return new(true);
    }

    public Task<TeamView?> FindAsync(
        Guid competitionId,
        Guid teamId,
        bool includePending,
        CancellationToken ct) =>
        FindCoreAsync(competitionId, teamId, includePending, includeInternal: true, ct);

    public Task<TeamView?> FindPublicAsync(
        Guid competitionId,
        Guid teamId,
        bool includePending,
        CancellationToken ct) =>
        FindCoreAsync(competitionId, teamId, includePending, includeInternal: false, ct);

    private async Task<TeamView?> FindCoreAsync(
        Guid competitionId,
        Guid teamId,
        bool includePending,
        bool includeInternal,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Mode,
                item.TracksEnabled,
                item.Tracks
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;
        var configuration = CompetitionTrackConfiguration.EffectiveFor(
            competition.Mode,
            competition.TracksEnabled,
            competition.Tracks);
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == teamId
            && item.CompetitionId == competitionId
            && item.DeletedAt == null
            && (includePending || item.RegistrationStatus == TeamRegistrationStatus.Approved),
            ct);
        if (team is null)
            return null;
        var track = configuration.Find(team.TrackKey);
        if (!includeInternal && track?.IsInternal == true)
            return null;
        return Map(team, configuration);
    }

    public async Task<TeamView?> FindForUserAsync(Guid competitionId, Guid userId, bool includePending, CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Mode,
                item.TracksEnabled,
                item.Tracks
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;
        var team = await db.Teams.AsNoTracking().Where(team => team.CompetitionId == competitionId
                && team.Members.Any(member => member.UserId == userId)
                && team.DeletedAt == null
                && (includePending || team.RegistrationStatus == TeamRegistrationStatus.Approved))
            .SingleOrDefaultAsync(ct);
        return team is null
            ? null
            : Map(team, CompetitionTrackConfiguration.EffectiveFor(
                competition.Mode,
                competition.TracksEnabled,
                competition.Tracks));
    }

    public async Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken ct) =>
        await db.Teams.AsNoTracking().AnyAsync(x => x.Id == teamId && x.CompetitionId == competitionId
            && x.DeletedAt == null && x.CaptainId == actorId, ct)
        || await db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId && x.Role == UserRole.Administrator, ct)
        || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId
            && x.OwnerId == actorId && x.DeletedAt == null, ct)
        || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId
            && x.Collaborators.Any(collaborator =>
                collaborator.Role == CompetitionCollaboratorRole.Manager
                && collaborator.UserId == actorId), ct);

    public async Task<TeamUpdateStoreResult> UpdateAsync(UpdateTeamCommand command, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await UpdateOnceAsync(command, ct);
            }
            catch (Exception exception) when (attempt < 2
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
        return new(null, TeamRegistrationFailure.TeamConflict);
    }

    private async Task<TeamUpdateStoreResult> UpdateOnceAsync(
        UpdateTeamCommand command,
        CancellationToken ct)
    {
        var name = command.Name.Trim();
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(
            db,
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            command.CompetitionId,
            ct);
        if (competition is null) return new(null, TeamRegistrationFailure.CompetitionNotFound);
        if (!ParticipantTeamMutationPolicy.CanChangeOrganization(
                competition.Status,
                competition.AllowTeamRegistrationWhileRunning))
            return new(null, TeamRegistrationFailure.RegistrationClosed);
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == command.TeamId
            && x.CompetitionId == command.CompetitionId && x.DeletedAt == null, ct);
        if (entity is null) return new(null, TeamRegistrationFailure.TeamNotFound);
        if (entity.IsBanned) return new(null, TeamRegistrationFailure.TeamBanned);
        if (entity.IsLocked) return new(null, TeamRegistrationFailure.TeamLocked);

        var configuration = CompetitionTrackConfiguration.FromPersisted(
            competition.Mode,
            competition.Tracks);
        var nextTrack = configuration.Find(entity.TrackKey) ?? configuration.DefaultTrack;
        var trackChanged = false;
        if (command.TrackKey is not null)
        {
            if (!competition.TracksEnabled)
                return new(null, TeamRegistrationFailure.TrackNotFound);
            var requestedKey = CompetitionTrackConfiguration.NormalizeKey(command.TrackKey);
            nextTrack = configuration.Find(requestedKey);
            if (nextTrack is null)
                return new(null, TeamRegistrationFailure.TrackNotFound);
            trackChanged = !string.Equals(
                entity.TrackKey,
                nextTrack.Key,
                StringComparison.OrdinalIgnoreCase);
            if (trackChanged && (nextTrack.IsInternal || !nextTrack.IsPublicSelectable))
                return new(null, TeamRegistrationFailure.TrackNotPublicSelectable);
        }

        var nameChanged = !string.Equals(entity.Name, name, StringComparison.Ordinal);
        if (!nameChanged && !trackChanged)
        {
            await transaction.CommitAsync(ct);
            return new(Map(entity, configuration));
        }

        var previousTrackKey = entity.TrackKey;
        entity.Name = name;
        if (trackChanged) entity.TrackKey = nextTrack.Key;
        var occurredAt = command.UpdatedAt ?? timeProvider.GetUtcNow();
        var actorUserId = command.ActorUserId ?? entity.CaptainId;
        if (nameChanged)
        {
            await events.RecordAsync(new(
                entity.CompetitionId,
                CompetitionEventKind.TeamUpdated,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Team,
                occurredAt,
                ActorUserId: actorUserId,
                TeamId: entity.Id,
                TeamRegistrationStatus: entity.RegistrationStatus), ct);
        }
        if (trackChanged)
        {
            await events.RecordAsync(new(
                entity.CompetitionId,
                CompetitionEventKind.TeamTrackChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Staff,
                occurredAt,
                ActorUserId: actorUserId,
                TeamId: entity.Id,
                TrackKey: entity.TrackKey,
                PreviousTrackKey: previousTrackKey), ct);
        }
        await ParticipantTeamRegistrationTransition.ApplyAsync(
            competition,
            entity,
            actorUserId,
            occurredAt,
            events,
            ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await transaction.FlushMessagesAsync(outbox);
        }
        catch (DbUpdateException exception) when (!RelationalRetry.IsTransientConcurrency(exception))
        {
            return new(null, TeamRegistrationFailure.TeamConflict);
        }
        return new(Map(entity, configuration));
    }

    public async Task<TeamRegistrationFailure?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var status = await CompetitionStateReader.ReadAsync(db, competitionId, ct);
        if (status is null) return TeamRegistrationFailure.CompetitionNotFound;
        if (status is CompetitionStatus.Running or CompetitionStatus.Paused) return TeamRegistrationFailure.CompetitionActive;
        if (status == CompetitionStatus.Finished) return TeamRegistrationFailure.CompetitionFinished;
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == teamId
            && x.CompetitionId == competitionId && x.DeletedAt == null, ct);
        if (entity is null) return TeamRegistrationFailure.TeamNotFound;
        if (entity.IsBanned) return TeamRegistrationFailure.TeamBanned;
        entity.DeletedAt = deletedAt;
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.TeamDeleted,
            CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Staff,
            deletedAt,
            ActorUserId: actorId,
            TeamId: teamId,
            TeamRegistrationStatus: entity.RegistrationStatus), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return null;
    }

    private static TeamView Map(Team x) => new(
        x.Id, x.CompetitionId, x.Name, x.AvatarFileId, x.CaptainId, x.MemberIds,
        x.RegistrationStatus, x.IsLocked, x.IsBanned, x.RegisteredAt, x.TrackKey, x.TrackKey);

    private static TeamView Map(Team team, CompetitionTrackConfiguration configuration) => new(
        team.Id,
        team.CompetitionId,
        team.Name,
        team.AvatarFileId,
        team.CaptainId,
        team.MemberIds,
        team.RegistrationStatus,
        team.IsLocked,
        team.IsBanned,
        team.RegisteredAt,
        team.TrackKey,
        configuration.Find(team.TrackKey)?.Name ?? team.TrackKey);

    private static string CreateInvitationToken()
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        Span<byte> random = stackalloc byte[64];
        Span<char> token = stackalloc char[32];
        var written = 0;
        while (written < token.Length)
        {
            System.Security.Cryptography.RandomNumberGenerator.Fill(random);
            foreach (var value in random)
            {
                if (value >= 248)
                    continue;
                token[written++] = alphabet[value % alphabet.Length];
                if (written == token.Length)
                    break;
            }
        }
        return new string(token);
    }
}
