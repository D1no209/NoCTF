using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfTeamMembershipStore(NoCtfDbContext db) : ITeamMembershipStore
{
    public async Task<TeamMembershipFailure?> JoinByInvitationAsync(
        Guid competitionId,
        string invitationToken,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AcquireMembershipLockAsync(competitionId, userId, ct);
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

        var competition = await db.Competitions.SingleOrDefaultAsync(
            item => item.Id == competitionId,
            ct);
        if (competition is null)
            return TeamMembershipFailure.CompetitionNotFound;
        if (TeamMembershipPolicy.IsMembershipChangeLocked(competition.Status))
            return TeamMembershipFailure.MembershipLocked;
        if (team.MemberIds.Length >= competition.MaxTeamMembers)
            return TeamMembershipFailure.TeamFull;

        team.MemberIds = [.. team.MemberIds, userId];
        await db.SaveChangesAsync(ct);
        await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    public async Task<(string? Token, TeamMembershipFailure? Failure)> RotateInvitationAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorId,
        string token,
        CancellationToken ct)
    {
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null)
            return (null, TeamMembershipFailure.TeamNotFound);
        if (team.CaptainId != actorId && !await IsManagerAsync(competitionId, actorId, ct))
            return (null, TeamMembershipFailure.TeamForbidden);

        team.InvitationToken = token;
        await db.SaveChangesAsync(ct);
        return (token, null);
    }

    public async Task<TeamMembershipFailure?> RemoveMemberAsync(
        Guid competitionId,
        Guid teamId,
        Guid targetUserId,
        Guid actorId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AcquireMembershipLockAsync(competitionId, targetUserId, ct);
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null)
            return TeamMembershipFailure.TeamNotFound;
        if (team.CaptainId == targetUserId)
            return TeamMembershipFailure.CaptainCannotBeRemoved;
        if (team.CaptainId != actorId && !await IsManagerAsync(competitionId, actorId, ct))
            return TeamMembershipFailure.TeamForbidden;
        if (!team.MemberIds.Contains(targetUserId))
            return TeamMembershipFailure.MemberNotFound;

        team.MemberIds = team.MemberIds.Where(id => id != targetUserId).ToArray();
        await db.SaveChangesAsync(ct);
        await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    public async Task<TeamMembershipFailure?> LeaveAsync(
        Guid competitionId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AcquireMembershipLockAsync(competitionId, userId, ct);
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
        await db.SaveChangesAsync(ct);
        await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    public async Task<TeamMembershipFailure?> TransferCaptainAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorId,
        Guid newCaptainId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AcquireMembershipLockAsync(competitionId, newCaptainId, ct);
        var team = await LoadAsync(competitionId, teamId, ct);
        if (team is null)
            return TeamMembershipFailure.TeamNotFound;
        if (team.CaptainId != actorId)
            return TeamMembershipFailure.CaptainOnly;
        if (!team.MemberIds.Contains(newCaptainId))
            return TeamMembershipFailure.MemberNotFound;

        team.CaptainId = newCaptainId;
        await db.SaveChangesAsync(ct);
        await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
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

    private Task<bool> IsManagerAsync(Guid competitionId, Guid userId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().AnyAsync(
            competition => competition.Id == competitionId
                && (competition.OwnerId == userId || competition.ManagerIds.Contains(userId)),
            ct);

    private Task AcquireMembershipLockAsync(
        Guid competitionId,
        Guid userId,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({'m' + competitionId.ToString("N") + userId.ToString("N")}, 0))",
            ct);
}
