using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Membership;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfTeamMembershipStore(NoCtfDbContext db) : ITeamMembershipStore
{
    public async Task<TeamMembershipFailure?> JoinByInvitationAsync(Guid competitionId, string invitationToken, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var team = await db.Teams.Include(item => item.Members).SingleOrDefaultAsync(item => item.CompetitionId == competitionId && item.InvitationToken == invitationToken, ct);
        if (team is null) return TeamMembershipFailure.TeamNotFound;
        if (team.Members.Any(member => member.UserId == userId)) return TeamMembershipFailure.UserAlreadyRegistered;
        var competition = await db.Competitions.SingleOrDefaultAsync(item => item.Id == competitionId, ct);
        if (competition is null) return TeamMembershipFailure.CompetitionNotFound;
        if (TeamMembershipPolicy.IsMembershipChangeLocked(competition.Status)) return TeamMembershipFailure.MembershipLocked;
        if (team.Members.Count >= competition.MaxTeamMembers) return TeamMembershipFailure.TeamFull;
        team.Members.Add(new() { Id = Guid.CreateVersion7(now), UserId = userId, MemberOrder = team.Members.Count, JoinedAt = now });
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<(string? Token, TeamMembershipFailure? Failure)> RotateInvitationAsync(Guid competitionId, Guid teamId, Guid actorId, string token, CancellationToken ct)
    {
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null) return (null, TeamMembershipFailure.TeamNotFound);
        if (!CanManage(team, actorId) && !await IsManagerAsync(competitionId, actorId, ct)) return (null, TeamMembershipFailure.TeamForbidden);
        team.InvitationToken = token;
        await db.SaveChangesAsync(ct);
        return (token, null);
    }
    public Task<InviteTeamMemberStoreResult> InviteAsync(InviteTeamMemberCommand command, CancellationToken ct) =>
        Task.FromResult(new InviteTeamMemberStoreResult(null, TeamMembershipFailure.InvitationConflict));

    public Task<TeamMembershipFailure?> RespondAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult<TeamMembershipFailure?>(TeamMembershipFailure.InvitationNotFound);

    public async Task<TeamMembershipFailure?> RemoveMemberAsync(Guid competitionId, Guid teamId, Guid targetUserId, Guid actorId, CancellationToken ct)
    {
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null) return TeamMembershipFailure.TeamNotFound;
        if (team.Members.OrderBy(member => member.MemberOrder).FirstOrDefault()?.UserId == targetUserId)
            return TeamMembershipFailure.CaptainCannotBeRemoved;
        if (!CanManage(team, actorId) && !await IsManagerAsync(competitionId, actorId, ct)) return TeamMembershipFailure.TeamForbidden;
        var member = team.Members.SingleOrDefault(item => item.UserId == targetUserId);
        if (member is null) return TeamMembershipFailure.MemberNotFound;
        team.Members.Remove(member);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<TeamMembershipFailure?> LeaveAsync(Guid competitionId, Guid userId, CancellationToken ct)
    {
        var team = await db.Teams.Include(item => item.Members)
            .SingleOrDefaultAsync(item => item.CompetitionId == competitionId && item.Members.Any(member => member.UserId == userId), ct);
        if (team is null) return TeamMembershipFailure.MembershipNotFound;
        if (team.Members.OrderBy(member => member.MemberOrder).First().UserId == userId) return TeamMembershipFailure.CaptainMustTransfer;
        team.Members.Remove(team.Members.Single(member => member.UserId == userId));
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<TeamMembershipFailure?> TransferCaptainAsync(Guid competitionId, Guid teamId, Guid actorId, Guid newCaptainId, CancellationToken ct)
    {
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null) return TeamMembershipFailure.TeamNotFound;
        var ordered = team.Members.OrderBy(member => member.MemberOrder).ToList();
        if (ordered.FirstOrDefault()?.UserId != actorId) return TeamMembershipFailure.CaptainOnly;
        var newCaptain = ordered.SingleOrDefault(member => member.UserId == newCaptainId);
        if (newCaptain is null) return TeamMembershipFailure.MemberNotFound;
        ordered.Remove(newCaptain);
        ordered.Insert(0, newCaptain);
        for (var index = 0; index < ordered.Count; index++) ordered[index].MemberOrder = index;
        await db.SaveChangesAsync(ct);
        return null;
    }

    private Task<NoCTF.Domain.Teams.Team?> LoadAsync(Guid competitionId, Guid teamId, CancellationToken ct) =>
        db.Teams.Include(item => item.Members).SingleOrDefaultAsync(item => item.Id == teamId && item.CompetitionId == competitionId, ct);

    private static bool CanManage(NoCTF.Domain.Teams.Team team, Guid userId) =>
        team.Members.OrderBy(member => member.MemberOrder).FirstOrDefault()?.UserId == userId;

    private Task<bool> IsManagerAsync(Guid competitionId, Guid userId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().AnyAsync(competition => competition.Id == competitionId
            && (competition.OwnerId == userId || competition.Collaborators.Any(collaborator => collaborator.UserId == userId && collaborator.Role == CompetitionCollaboratorRole.Manager)), ct);
}
