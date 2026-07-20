using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Membership;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfTeamMembershipStore(NoCtfDbContext db) : ITeamMembershipStore
{
    public async Task<InviteTeamMemberStoreResult> InviteAsync(InviteTeamMemberCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var competitionStatus = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (competitionStatus is null) return new(null, TeamMembershipFailure.CompetitionNotFound);
        if (TeamMembershipPolicy.IsMembershipChangeLocked(competitionStatus.Value)) return new(null, TeamMembershipFailure.MembershipLocked);
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.TeamId && x.CompetitionId == command.CompetitionId && !x.Deletion.IsDeleted, ct);
        if (team is null) return new(null, TeamMembershipFailure.TeamNotFound);
        var canInvite = team.CaptainId == command.ActorId || await db.Competitions.AsNoTracking().AnyAsync(x =>
            x.Id == command.CompetitionId && x.Collaborators.Any(collaborator => collaborator.UserId == command.ActorId && collaborator.Role == CompetitionCollaboratorRole.Manager), ct)
            || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == command.CompetitionId && x.OwnerId == command.ActorId, ct);
        if (!canInvite) return new(null, TeamMembershipFailure.TeamForbidden);
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == command.CompetitionId, ct);
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == command.InvitedUserId, ct)) return new(null, TeamMembershipFailure.UserNotFound);
        if (await db.TeamMembers.AsNoTracking().AnyAsync(x => x.CompetitionId == command.CompetitionId && x.UserId == command.InvitedUserId, ct)) return new(null, TeamMembershipFailure.UserAlreadyRegistered);
        if (await db.TeamMembers.AsNoTracking().CountAsync(x => x.TeamId == command.TeamId, ct) >= competition.MaxTeamMembers) return new(null, TeamMembershipFailure.TeamFull);
        var invitation = new TeamInvitation
        {
            Id = Guid.CreateVersion7(command.Now), CompetitionId = command.CompetitionId, TeamId = command.TeamId,
            InvitedUserId = command.InvitedUserId, InvitedById = command.ActorId, Status = TeamInvitationStatus.Pending,
            ExpiresAt = command.ExpiresAt, CreatedAt = command.Now
        };
        db.TeamInvitations.Add(invitation);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException) { await transaction.RollbackAsync(ct); return new(null, TeamMembershipFailure.InvitationConflict); }
        return new(new(invitation.Id, invitation.CompetitionId, invitation.TeamId, invitation.InvitedUserId, invitation.ExpiresAt, invitation.CreatedAt));
    }

    public async Task<TeamMembershipFailure?> RespondAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var competitionId = await db.TeamInvitations.AsNoTracking().Where(x => x.Id == invitationId && x.InvitedUserId == userId)
            .Select(x => (Guid?)x.CompetitionId).SingleOrDefaultAsync(ct);
        if (competitionId is null) return TeamMembershipFailure.InvitationNotFound;
        var competitionStatus = await CompetitionWriteLock.AcquireAsync(db, competitionId.Value, ct);
        if (competitionStatus is null) return TeamMembershipFailure.CompetitionNotFound;
        if (TeamMembershipPolicy.IsInvitationResponseLocked(competitionStatus.Value, accept))
            return TeamMembershipFailure.MembershipLocked;
        var invitation = await db.TeamInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.InvitedUserId == userId, ct);
        if (invitation is null) return TeamMembershipFailure.InvitationNotFound;
        if (invitation.Status != TeamInvitationStatus.Pending) return TeamMembershipFailure.InvitationAlreadyAnswered;
        if (invitation.ExpiresAt <= now) return TeamMembershipFailure.InvitationExpired;
        if (!accept)
        {
            invitation.Status = TeamInvitationStatus.Rejected; invitation.RespondedAt = now;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
        }
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == invitation.CompetitionId, ct);
        if (await db.TeamMembers.AnyAsync(x => x.CompetitionId == invitation.CompetitionId && x.UserId == userId, ct)) return TeamMembershipFailure.UserAlreadyRegistered;
        if (await db.TeamMembers.CountAsync(x => x.TeamId == invitation.TeamId, ct) >= competition.MaxTeamMembers) return TeamMembershipFailure.TeamFull;
        db.TeamMembers.Add(new TeamMember { Id = Guid.CreateVersion7(now), CompetitionId = invitation.CompetitionId,
            TeamId = invitation.TeamId, UserId = userId, Role = TeamMemberRole.Member, JoinedAt = now });
        invitation.Status = TeamInvitationStatus.Accepted; invitation.RespondedAt = now;
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException) { await transaction.RollbackAsync(ct); return TeamMembershipFailure.MembershipConflict; }
        return null;
    }

    public async Task<TeamMembershipFailure?> RemoveMemberAsync(Guid competitionId, Guid teamId, Guid targetUserId, Guid actorId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return TeamMembershipFailure.CompetitionNotFound;
        if (TeamMembershipPolicy.IsMembershipChangeLocked(status.Value)) return TeamMembershipFailure.MembershipLocked;
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (team is null) return TeamMembershipFailure.TeamNotFound;
        if (team.CaptainId == targetUserId) return TeamMembershipFailure.CaptainCannotBeRemoved;
        var canManage = team.CaptainId == actorId || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId && x.OwnerId == actorId, ct)
            || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId
                && x.Collaborators.Any(collaborator => collaborator.UserId == actorId && collaborator.Role == CompetitionCollaboratorRole.Manager), ct);
        if (!canManage) return TeamMembershipFailure.TeamForbidden;
        var member = await db.TeamMembers.SingleOrDefaultAsync(x => x.TeamId == teamId && x.UserId == targetUserId, ct);
        if (member is null) return TeamMembershipFailure.MemberNotFound;
        db.TeamMembers.Remove(member); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
    }

    public async Task<TeamMembershipFailure?> LeaveAsync(Guid competitionId, Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return TeamMembershipFailure.CompetitionNotFound;
        if (TeamMembershipPolicy.IsMembershipChangeLocked(status.Value)) return TeamMembershipFailure.MembershipLocked;
        var member = await db.TeamMembers.SingleOrDefaultAsync(x => x.CompetitionId == competitionId && x.UserId == userId, ct);
        if (member is null) return TeamMembershipFailure.MembershipNotFound;
        if (member.Role == TeamMemberRole.Captain) return TeamMembershipFailure.CaptainMustTransfer;
        db.TeamMembers.Remove(member); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
    }

    public async Task<TeamMembershipFailure?> TransferCaptainAsync(Guid competitionId, Guid teamId, Guid actorId, Guid newCaptainId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return TeamMembershipFailure.CompetitionNotFound;
        if (TeamMembershipPolicy.IsMembershipChangeLocked(status.Value)) return TeamMembershipFailure.MembershipLocked;
        var team = await db.Teams.SingleOrDefaultAsync(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (team is null) return TeamMembershipFailure.TeamNotFound;
        if (team.CaptainId != actorId) return TeamMembershipFailure.CaptainOnly;
        var oldCaptain = await db.TeamMembers.SingleAsync(x => x.TeamId == teamId && x.UserId == actorId, ct);
        var newCaptain = await db.TeamMembers.SingleOrDefaultAsync(x => x.TeamId == teamId && x.UserId == newCaptainId, ct);
        if (newCaptain is null) return TeamMembershipFailure.MemberNotFound;
        oldCaptain.Role = TeamMemberRole.Member; newCaptain.Role = TeamMemberRole.Captain; team.CaptainId = newCaptainId;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
    }

}
