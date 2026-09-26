using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Registration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Teams;
using NoCTF.Infrastructure.Teams.Registration;
using NoCTF.Infrastructure.Competitions.Progression;

namespace NoCTF.Infrastructure.Teams.Membership;

public sealed class TeamMembershipStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    TimeProvider? clock = null,
    ICompetitionEventRecorder? eventRecorder = null,
    ProgressionReconciler? progressionReconciler = null) : ITeamMembershipStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public async Task<TeamMembershipFailure?> JoinByInvitationAsync(
        Guid competitionId,
        string invitationToken,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await JoinOnceAsync(
                    competitionId,
                    invitationToken,
                    userId,
                    now,
                    ct);
            }
            catch (Exception exception) when (attempt < 2
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)),
                    ct);
            }
        }
        return TeamMembershipFailure.MembershipConflict;
    }

    private async Task<TeamMembershipFailure?> JoinOnceAsync(
        Guid competitionId,
        string invitationToken,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db,
            System.Data.IsolationLevel.Serializable,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return TeamMembershipFailure.CompetitionNotFound;
        var team = await db.Teams.SingleOrDefaultAsync(
            item => item.CompetitionId == competitionId
                && item.InvitationToken == invitationToken
                && item.DeletedAt == null,
            ct);
        if (team is null)
            return TeamMembershipFailure.TeamNotFound;
        if (team.IsBanned)
            return TeamMembershipFailure.TeamBanned;
        var userIdentity = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.Id })
            .SingleOrDefaultAsync(ct);
        if (userIdentity is null)
            return TeamMembershipFailure.MemberNotFound;
        if (team.Members.Any(member => member.UserId == userId))
            return TeamMembershipFailure.UserAlreadyRegistered;
        if (await db.Teams.AnyAsync(
                item => item.CompetitionId == competitionId
                    && item.DeletedAt == null
                    && item.Members.Any(member => member.UserId == userId),
                ct))
            return TeamMembershipFailure.UserAlreadyRegistered;

        if (!ParticipantTeamMutationPolicy.CanChangeOrganization(
                competition.Status,
                competition.AllowTeamRegistrationWhileRunning))
            return TeamMembershipFailure.MembershipLocked;
        if (team.MemberIds.Length >= competition.MaxTeamMembers)
            return TeamMembershipFailure.TeamFull;

        team.MemberIds = [.. team.MemberIds, userId];
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.TeamMemberJoined,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            now,
            ActorUserId: userId,
            RelatedUserId: userId,
            TeamId: team.Id), ct);
        await ParticipantTeamRegistrationTransition.ApplyAsync(
            competition,
            team,
            userId,
            now,
            events,
            ct);
        await db.SaveChangesAsync(ct);
        await SyncProgressionAsync(competitionId, team.Id, now, ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return null;
    }

    public async Task<(string? Token, TeamMembershipFailure? Failure)> RotateInvitationAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorId,
        string token,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db,
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return (null, TeamMembershipFailure.CompetitionNotFound);
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null)
            return (null, TeamMembershipFailure.TeamNotFound);
        if (team.IsBanned)
            return (null, TeamMembershipFailure.TeamBanned);
        if (team.CaptainId != actorId && !IsManager(competition, actorId))
            return (null, TeamMembershipFailure.TeamForbidden);

        team.InvitationToken = token;
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.TeamUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            timeProvider.GetUtcNow(),
            ActorUserId: actorId,
            TeamId: teamId), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return (token, null);
    }

    public async Task<(string? Token, TeamMembershipFailure? Failure)> GetInvitationAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorId,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == competitionId, ct);
        if (competition is null)
            return (null, TeamMembershipFailure.CompetitionNotFound);
        var team = await db.Teams.AsNoTracking()
            .Where(item => item.Id == teamId && item.CompetitionId == competitionId)
            .Select(item => new
            {
                item.CaptainId,
                item.InvitationToken,
                item.IsBanned
            })
            .SingleOrDefaultAsync(ct);
        if (team is null)
            return (null, TeamMembershipFailure.TeamNotFound);
        if (team.IsBanned)
            return (null, TeamMembershipFailure.TeamBanned);
        if (team.CaptainId != actorId && !IsManager(competition, actorId))
            return (null, TeamMembershipFailure.TeamForbidden);
        return (team.InvitationToken, null);
    }

    public async Task<TeamMembershipFailure?> RemoveMemberAsync(
        Guid competitionId,
        Guid teamId,
        Guid targetUserId,
        Guid actorId,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db,
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return TeamMembershipFailure.CompetitionNotFound;
        if (!ParticipantTeamMutationPolicy.CanChangeOrganization(
                competition.Status,
                competition.AllowTeamRegistrationWhileRunning))
            return TeamMembershipFailure.MembershipLocked;
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null)
            return TeamMembershipFailure.TeamNotFound;
        if (team.IsBanned)
            return TeamMembershipFailure.TeamBanned;
        if (team.CaptainId == targetUserId)
            return TeamMembershipFailure.CaptainCannotBeRemoved;
        if (team.CaptainId != actorId && !IsManager(competition, actorId))
            return TeamMembershipFailure.TeamForbidden;
        if (!team.Members.Any(member => member.UserId == targetUserId))
            return TeamMembershipFailure.MemberNotFound;

        team.MemberIds = team.MemberIds.Where(id => id != targetUserId).ToArray();
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.TeamMemberRemoved,
            CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Team,
            timeProvider.GetUtcNow(),
            ActorUserId: actorId,
            RelatedUserId: targetUserId,
            TeamId: teamId), ct);
        await ParticipantTeamRegistrationTransition.ApplyAsync(
            competition,
            team,
            actorId,
            timeProvider.GetUtcNow(),
            events,
            ct);
        await db.SaveChangesAsync(ct);
        await SyncProgressionAsync(competitionId, team.Id, timeProvider.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return null;
    }

    public async Task<TeamMembershipFailure?> LeaveAsync(
        Guid competitionId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db,
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return TeamMembershipFailure.CompetitionNotFound;
        if (!ParticipantTeamMutationPolicy.CanChangeOrganization(
                competition.Status,
                competition.AllowTeamRegistrationWhileRunning))
            return TeamMembershipFailure.MembershipLocked;
        var team = await db.Teams.SingleOrDefaultAsync(
            item => item.CompetitionId == competitionId
                && item.DeletedAt == null
                && item.Members.Any(member => member.UserId == userId),
            ct);
        if (team is null)
            return TeamMembershipFailure.MembershipNotFound;
        if (team.IsBanned)
            return TeamMembershipFailure.TeamBanned;
        if (team.CaptainId == userId)
            return TeamMembershipFailure.CaptainMustTransfer;

        team.MemberIds = team.MemberIds.Where(id => id != userId).ToArray();
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.TeamMemberRemoved,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            timeProvider.GetUtcNow(),
            ActorUserId: userId,
            RelatedUserId: userId,
            TeamId: team.Id), ct);
        await ParticipantTeamRegistrationTransition.ApplyAsync(
            competition,
            team,
            userId,
            timeProvider.GetUtcNow(),
            events,
            ct);
        await db.SaveChangesAsync(ct);
        await SyncProgressionAsync(competitionId, team.Id, timeProvider.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return null;
    }

    public async Task<TeamMembershipFailure?> TransferCaptainAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorId,
        Guid newCaptainId,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db,
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return TeamMembershipFailure.CompetitionNotFound;
        if (!ParticipantTeamMutationPolicy.CanChangeOrganization(
                competition.Status,
                competition.AllowTeamRegistrationWhileRunning))
            return TeamMembershipFailure.MembershipLocked;
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null)
            return TeamMembershipFailure.TeamNotFound;
        if (team.IsBanned)
            return TeamMembershipFailure.TeamBanned;
        if (team.CaptainId != actorId)
            return TeamMembershipFailure.CaptainOnly;
        if (!team.Members.Any(member => member.UserId == newCaptainId))
            return TeamMembershipFailure.MemberNotFound;

        team.CaptainId = newCaptainId;
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.TeamCaptainTransferred,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            timeProvider.GetUtcNow(),
            ActorUserId: actorId,
            RelatedUserId: newCaptainId,
            TeamId: teamId), ct);
        await ParticipantTeamRegistrationTransition.ApplyAsync(
            competition,
            team,
            actorId,
            timeProvider.GetUtcNow(),
            events,
            ct);
        await db.SaveChangesAsync(ct);
        await SyncProgressionAsync(competitionId, team.Id, timeProvider.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return null;
    }

    private Task SyncProgressionAsync(
        Guid competitionId, Guid teamId, DateTimeOffset now, CancellationToken ct) =>
        (progressionReconciler ?? new ProgressionReconciler(db))
            .ReconcilePersistedTeamAsync(competitionId, teamId, now, ct);

    private Task<NoCTF.Domain.Teams.Team?> LoadAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct) =>
        db.Teams.SingleOrDefaultAsync(
            item => item.Id == teamId
                && item.CompetitionId == competitionId
                && item.DeletedAt == null,
            ct);

    private static bool IsManager(Competition competition, Guid userId) =>
        competition.OwnerId == userId || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId);
}
