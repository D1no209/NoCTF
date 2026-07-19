using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Membership;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfTeamMembershipStore(NoCtfDbContext db) : ITeamMembershipStore
{
    public async Task<(TeamInvitationView? Invitation, string? Error)> InviteAsync(InviteTeamMemberCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.TeamId && x.CompetitionId == command.CompetitionId && !x.Deletion.IsDeleted, ct);
        if (team is null) return (null, "team_not_found");
        var canInvite = team.CaptainId == command.ActorId || await db.CompetitionCollaborators.AsNoTracking().AnyAsync(x =>
            x.CompetitionId == command.CompetitionId && x.UserId == command.ActorId && x.Role == CompetitionCollaboratorRole.Manager, ct)
            || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == command.CompetitionId && x.OwnerId == command.ActorId, ct);
        if (!canInvite) return (null, "team_forbidden");
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == command.CompetitionId, ct);
        if (competition.Status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished) return (null, "membership_locked");
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == command.InvitedUserId, ct)) return (null, "user_not_found");
        if (await db.TeamMembers.AsNoTracking().AnyAsync(x => x.CompetitionId == command.CompetitionId && x.UserId == command.InvitedUserId, ct)) return (null, "user_already_registered");
        if (await db.TeamMembers.AsNoTracking().CountAsync(x => x.TeamId == command.TeamId, ct) >= competition.MaxTeamMembers) return (null, "team_full");
        var invitation = new TeamInvitation
        {
            Id = Guid.CreateVersion7(command.Now), CompetitionId = command.CompetitionId, TeamId = command.TeamId,
            InvitedUserId = command.InvitedUserId, InvitedById = command.ActorId, Status = TeamInvitationStatus.Pending,
            ExpiresAt = command.ExpiresAt, CreatedAt = command.Now
        };
        db.TeamInvitations.Add(invitation);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException) { await transaction.RollbackAsync(ct); return (null, "invitation_conflict"); }
        return (new(invitation.Id, invitation.CompetitionId, invitation.TeamId, invitation.InvitedUserId, invitation.ExpiresAt, invitation.CreatedAt), null);
    }

    public async Task<string?> RespondAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var invitation = await db.TeamInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.InvitedUserId == userId, ct);
        if (invitation is null) return "invitation_not_found";
        if (invitation.Status != TeamInvitationStatus.Pending) return "invitation_already_answered";
        if (invitation.ExpiresAt <= now) return "invitation_expired";
        if (!accept)
        {
            invitation.Status = TeamInvitationStatus.Rejected; invitation.RespondedAt = now;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
        }
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == invitation.CompetitionId, ct);
        if (competition.Status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished) return "membership_locked";
        if (await db.TeamMembers.AnyAsync(x => x.CompetitionId == invitation.CompetitionId && x.UserId == userId, ct)) return "user_already_registered";
        if (await db.TeamMembers.CountAsync(x => x.TeamId == invitation.TeamId, ct) >= competition.MaxTeamMembers) return "team_full";
        db.TeamMembers.Add(new TeamMember { Id = Guid.CreateVersion7(now), CompetitionId = invitation.CompetitionId,
            TeamId = invitation.TeamId, UserId = userId, Role = TeamMemberRole.Member, JoinedAt = now });
        invitation.Status = TeamInvitationStatus.Accepted; invitation.RespondedAt = now;
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException) { await transaction.RollbackAsync(ct); return "membership_conflict"; }
        return null;
    }

    public async Task<string?> RemoveMemberAsync(Guid competitionId, Guid teamId, Guid targetUserId, Guid actorId, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (team is null) return "team_not_found";
        if (team.CaptainId == targetUserId) return "captain_cannot_be_removed";
        var canManage = team.CaptainId == actorId || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId && x.OwnerId == actorId, ct)
            || await db.CompetitionCollaborators.AsNoTracking().AnyAsync(x => x.CompetitionId == competitionId && x.UserId == actorId && x.Role == CompetitionCollaboratorRole.Manager, ct);
        if (!canManage) return "team_forbidden";
        if (await IsMembershipLocked(competitionId, ct)) return "membership_locked";
        var member = await db.TeamMembers.SingleOrDefaultAsync(x => x.TeamId == teamId && x.UserId == targetUserId, ct);
        if (member is null) return "member_not_found";
        db.TeamMembers.Remove(member); await db.SaveChangesAsync(ct); return null;
    }

    public async Task<string?> LeaveAsync(Guid competitionId, Guid userId, CancellationToken ct)
    {
        if (await IsMembershipLocked(competitionId, ct)) return "membership_locked";
        var member = await db.TeamMembers.SingleOrDefaultAsync(x => x.CompetitionId == competitionId && x.UserId == userId, ct);
        if (member is null) return "membership_not_found";
        if (member.Role == TeamMemberRole.Captain) return "captain_must_transfer";
        db.TeamMembers.Remove(member); await db.SaveChangesAsync(ct); return null;
    }

    public async Task<string?> TransferCaptainAsync(Guid competitionId, Guid teamId, Guid actorId, Guid newCaptainId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var team = await db.Teams.SingleOrDefaultAsync(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (team is null) return "team_not_found";
        if (team.CaptainId != actorId) return "captain_only";
        if (await IsMembershipLocked(competitionId, ct)) return "membership_locked";
        var oldCaptain = await db.TeamMembers.SingleAsync(x => x.TeamId == teamId && x.UserId == actorId, ct);
        var newCaptain = await db.TeamMembers.SingleOrDefaultAsync(x => x.TeamId == teamId && x.UserId == newCaptainId, ct);
        if (newCaptain is null) return "member_not_found";
        oldCaptain.Role = TeamMemberRole.Member; newCaptain.Role = TeamMemberRole.Captain; team.CaptainId = newCaptainId;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
    }

    private Task<bool> IsMembershipLocked(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId
            && (x.Status == CompetitionStatus.Running || x.Status == CompetitionStatus.Paused
                || x.Status == CompetitionStatus.Finished), ct);
}
