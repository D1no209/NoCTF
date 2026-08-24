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
    ITransactionalMessageOutbox outbox,
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
                x.TrackConfigurationJson))
            .SingleOrDefaultAsync(ct);

    public async Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken ct)
    {
        var name = command.Name.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            command.CompetitionId,
            ct);
        if (competition is null) return new(null, TeamRegistrationFailure.CompetitionNotFound);
        if (competition.Status is CompetitionStatus.Paused or CompetitionStatus.Finished
            || competition.Status == CompetitionStatus.Running
                && !competition.AllowTeamRegistrationWhileRunning)
            return new(null, TeamRegistrationFailure.RegistrationClosed);
        var tracks = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
        var requestedTrackKey = CompetitionTrackConfiguration.NormalizeKey(command.TrackKey);
        if (requestedTrackKey is null)
            return new(null, TeamRegistrationFailure.TrackNotFound);
        var track = tracks.Find(requestedTrackKey);
        if (track is null)
            return new(null, TeamRegistrationFailure.TrackNotFound);
        if (track.IsInternal || !track.IsPublicSelectable)
            return new(null, TeamRegistrationFailure.TrackNotPublicSelectable);
        if (track.RequiresInvitationCode
            && string.IsNullOrWhiteSpace(command.TrackInvitationCode))
        {
            return new(null, TeamRegistrationFailure.TrackInvitationRequired);
        }
        if (track.RequiresInvitationCode
            && !CompetitionTrackInvitationCode.Verify(
                track.InvitationCode,
                command.TrackInvitationCode))
        {
            return new(null, TeamRegistrationFailure.TrackInvitationInvalid);
        }
        if (!await db.Users.AsNoTracking().AnyAsync(user => user.Id == command.UserId, ct))
            return new(null, TeamRegistrationFailure.TeamNameOrMembershipConflict);
        if (await db.Teams.AnyAsync(x => x.CompetitionId == command.CompetitionId
            && x.DeletedAt == null
            && x.MemberIds.Contains(command.UserId), ct))
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
            RegistrationStatus = status,
            RegisteredAt = command.RegisteredAt
        };
        db.Teams.Add(team);
        await events.RecordAsync(new(
            team.CompetitionId,
            CompetitionEventKind.TeamRegistered,
            CompetitionEventLevel.Information,
            status == TeamRegistrationStatus.Approved && !track.IsInternal
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Staff,
            command.RegisteredAt,
            ActorUserId: command.UserId,
            RelatedUserId: command.UserId,
            TeamId: team.Id,
            TeamRegistrationStatus: status,
            TrackKey: track.Key), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            return new(null, TeamRegistrationFailure.TeamNameOrMembershipConflict);
        }
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
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

    private async Task<IReadOnlyList<TeamView>> ListCoreAsync(
        Guid competitionId,
        bool includePending,
        bool includeInternal,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new { item.Mode, item.TrackConfigurationJson })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return [];
        var configuration = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new { item.Status, item.Mode, item.TrackConfigurationJson })
            .SingleOrDefaultAsync(ct);
        if (competition is null) return new(false, TeamRegistrationFailure.CompetitionNotFound);
        if (competition.Status == CompetitionStatus.Finished) return new(false, TeamRegistrationFailure.CompetitionFinished);
        var team = await db.Teams.AsNoTracking().Where(item => item.Id == teamId
                && item.CompetitionId == competitionId && item.DeletedAt == null)
            .Select(item => new { item.TrackKey })
            .SingleOrDefaultAsync(ct);
        if (team is null) return new(false, TeamRegistrationFailure.TeamNotFound);
        var track = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson).Find(team.TrackKey);
        var changed = await db.Teams.Where(x => x.Id == teamId && x.CompetitionId == competitionId
                && x.RegistrationStatus == TeamRegistrationStatus.Pending)
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
        await outbox.FlushOutgoingMessagesAsync();
        return changed == 1 ? new(true) : new(false, TeamRegistrationFailure.TeamReviewConflict);
    }

    public async Task<TeamReviewStoreResult> ResubmitAsync(
        Guid competitionId,
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Status,
                item.AllowTeamRegistrationWhileRunning
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return new(false, TeamRegistrationFailure.CompetitionNotFound);
        if (competition.Status is CompetitionStatus.Paused or CompetitionStatus.Finished
            || competition.Status == CompetitionStatus.Running
                && !competition.AllowTeamRegistrationWhileRunning)
            return new(false, TeamRegistrationFailure.RegistrationClosed);
        var changed = await db.Teams
            .Where(team =>
                team.Id == teamId &&
                team.CompetitionId == competitionId &&
                team.CaptainId == userId &&
                team.RegistrationStatus == TeamRegistrationStatus.Rejected)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    team => team.RegistrationStatus,
                    TeamRegistrationStatus.Pending),
                ct);
        if (changed == 1)
        {
            await events.RecordAsync(new(
                competitionId,
                CompetitionEventKind.TeamRegistrationChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Staff,
                timeProvider.GetUtcNow(),
                ActorUserId: userId,
                RelatedUserId: userId,
                TeamId: teamId,
                TeamRegistrationStatus: TeamRegistrationStatus.Pending), ct);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return changed == 1
            ? new(true)
            : new(false, TeamRegistrationFailure.TeamReviewConflict);
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
            .Select(item => new { item.Mode, item.TrackConfigurationJson })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;
        var configuration = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
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
            .Select(item => new { item.Mode, item.TrackConfigurationJson })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;
        var team = await db.Teams.AsNoTracking().Where(team => team.CompetitionId == competitionId
                && team.MemberIds.Contains(userId)
                && team.DeletedAt == null
                && (includePending || team.RegistrationStatus == TeamRegistrationStatus.Approved))
            .SingleOrDefaultAsync(ct);
        return team is null
            ? null
            : Map(team, CompetitionTrackConfiguration.ParseOrDefault(
                competition.Mode,
                competition.TrackConfigurationJson));
    }

    public async Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken ct) =>
        await db.Teams.AsNoTracking().AnyAsync(x => x.Id == teamId && x.CompetitionId == competitionId
            && x.DeletedAt == null && x.CaptainId == actorId, ct)
        || await db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId && x.Role == UserRole.Administrator, ct)
        || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId
            && x.OwnerId == actorId && x.DeletedAt == null, ct)
        || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId
            && x.ManagerIds.Contains(actorId), ct);

    public async Task<TeamUpdateStoreResult> UpdateAsync(UpdateTeamCommand command, CancellationToken ct)
    {
        var name = command.Name.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionStateReader.ReadAsync(db, command.CompetitionId, ct);
        if (status is null) return new(null, TeamRegistrationFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished) return new(null, TeamRegistrationFailure.CompetitionFinished);
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == command.TeamId
            && x.CompetitionId == command.CompetitionId && x.DeletedAt == null, ct);
        if (entity is null) return new(null, TeamRegistrationFailure.TeamNotFound);
        if (entity.IsLocked) return new(null, TeamRegistrationFailure.TeamLocked);
        entity.Name = name;
        await events.RecordAsync(new(
            entity.CompetitionId,
            CompetitionEventKind.TeamUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            timeProvider.GetUtcNow(),
            TeamId: entity.Id,
            TeamRegistrationStatus: entity.RegistrationStatus), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
        }
        catch (DbUpdateException) { return new(null, TeamRegistrationFailure.TeamConflict); }
        return new(Map(entity));
    }

    public async Task<TeamRegistrationFailure?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionStateReader.ReadAsync(db, competitionId, ct);
        if (status is null) return TeamRegistrationFailure.CompetitionNotFound;
        if (status is CompetitionStatus.Running or CompetitionStatus.Paused) return TeamRegistrationFailure.CompetitionActive;
        if (status == CompetitionStatus.Finished) return TeamRegistrationFailure.CompetitionFinished;
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == teamId
            && x.CompetitionId == competitionId && x.DeletedAt == null, ct);
        if (entity is null) return TeamRegistrationFailure.TeamNotFound;
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
        await outbox.FlushOutgoingMessagesAsync();
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
