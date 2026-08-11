using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Teams;

namespace NoCTF.Infrastructure.Teams.Membership;

public sealed class TeamMembershipStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    TimeProvider? clock = null,
    ICompetitionEventRecorder? eventRecorder = null) : ITeamMembershipStore
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
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
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
        if (team.MemberIds.Contains(userId))
            return TeamMembershipFailure.UserAlreadyRegistered;
        if (await db.Teams.AnyAsync(
                item => item.CompetitionId == competitionId
                    && item.DeletedAt == null
                    && item.MemberIds.Contains(userId),
                ct))
            return TeamMembershipFailure.UserAlreadyRegistered;

        if (TeamMembershipPolicy.IsMembershipChangeLocked(competition.Status))
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
        competition.LeaderboardDirty = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return null;
    }

    public async Task<(string? Token, TeamMembershipFailure? Failure)> RotateInvitationAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorId,
        string token,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
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
        await outbox.FlushOutgoingMessagesAsync();
        return (token, null);
    }

    public async Task<TeamMembershipFailure?> RemoveMemberAsync(
        Guid competitionId,
        Guid teamId,
        Guid targetUserId,
        Guid actorId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return TeamMembershipFailure.CompetitionNotFound;
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null)
            return TeamMembershipFailure.TeamNotFound;
        if (team.CaptainId == targetUserId)
            return TeamMembershipFailure.CaptainCannotBeRemoved;
        if (team.CaptainId != actorId && !IsManager(competition, actorId))
            return TeamMembershipFailure.TeamForbidden;
        if (!team.MemberIds.Contains(targetUserId))
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
        competition.LeaderboardDirty = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return null;
    }

    public async Task<TeamMembershipFailure?> LeaveAsync(
        Guid competitionId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return TeamMembershipFailure.CompetitionNotFound;
        var team = await db.Teams.SingleOrDefaultAsync(
            item => item.CompetitionId == competitionId
                && item.DeletedAt == null
                && item.MemberIds.Contains(userId),
            ct);
        if (team is null)
            return TeamMembershipFailure.MembershipNotFound;
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
        competition.LeaderboardDirty = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return null;
    }

    public async Task<TeamMembershipFailure?> TransferCaptainAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorId,
        Guid newCaptainId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(
            db,
            competitionId,
            ct);
        if (competition is null)
            return TeamMembershipFailure.CompetitionNotFound;
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null)
            return TeamMembershipFailure.TeamNotFound;
        if (team.CaptainId != actorId)
            return TeamMembershipFailure.CaptainOnly;
        if (!team.MemberIds.Contains(newCaptainId))
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
        competition.LeaderboardDirty = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return null;
    }

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
        competition.OwnerId == userId || competition.ManagerIds.Contains(userId);
}
