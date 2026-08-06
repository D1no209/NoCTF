using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Profiles;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Teams.Profiles;

public sealed class TeamProfileStore(NoCtfDbContext db) : ITeamProfileStore
{
    public async Task<TeamProfileMutationResult> CreateAsync(
        CreateTeamProfileCommand command,
        CancellationToken cancellationToken)
    {
        var team = new TeamProfile
        {
            Id = Guid.CreateVersion7(command.CreatedAt),
            Name = command.Name,
            NormalizedName = command.Name.ToUpperInvariant(),
            AvatarUrl = command.AvatarUrl,
            CaptainId = command.UserId,
            MemberIds = [command.UserId],
            InvitationToken = CreateInvitationToken(),
            CreatedAt = command.CreatedAt
        };
        db.TeamProfiles.Add(team);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(Map(team));
        }
        catch (DbUpdateException)
        {
            return new(Failure: TeamProfileFailure.TeamNameConflict);
        }
    }

    public async Task<IReadOnlyList<TeamProfileView>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await db.TeamProfiles.AsNoTracking()
            .Where(profile => profile.MemberIds.Contains(userId))
            .OrderBy(profile => profile.Name)
            .Select(profile => new TeamProfileView(
                profile.Id,
                profile.Name,
                profile.AvatarUrl,
                profile.CaptainId,
                profile.MemberIds,
                profile.InvitationToken,
                profile.CreatedAt))
            .ToArrayAsync(cancellationToken);

    public Task<TeamProfileView?> FindAsync(
        Guid teamId,
        CancellationToken cancellationToken) =>
        db.TeamProfiles.AsNoTracking()
            .Where(profile => profile.Id == teamId)
            .Select(profile => new TeamProfileView(
                profile.Id,
                profile.Name,
                profile.AvatarUrl,
                profile.CaptainId,
                profile.MemberIds,
                profile.InvitationToken,
                profile.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> CanManageAsync(
        Guid actorId,
        Guid teamId,
        CancellationToken cancellationToken) =>
        await db.TeamProfiles.AsNoTracking().AnyAsync(
            profile => profile.Id == teamId && profile.CaptainId == actorId,
            cancellationToken)
        || await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == actorId && user.Role == UserRole.Administrator,
            cancellationToken);

    public async Task<TeamProfileMutationResult> UpdateAsync(
        UpdateTeamProfileCommand command,
        CancellationToken cancellationToken)
    {
        var team = await db.TeamProfiles.SingleOrDefaultAsync(
            profile => profile.Id == command.TeamId,
            cancellationToken);
        if (team is null)
            return new(Failure: TeamProfileFailure.TeamNotFound);
        team.Name = command.Name;
        team.NormalizedName = command.Name.ToUpperInvariant();
        team.AvatarUrl = command.AvatarUrl;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(Map(team));
        }
        catch (DbUpdateException)
        {
            return new(Failure: TeamProfileFailure.TeamNameConflict);
        }
    }

    public async Task<TeamProfileMutationResult> JoinAsync(
        string invitationToken,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({'g' + userId.ToString("N")}, 0))",
            cancellationToken);
        var team = await db.TeamProfiles.SingleOrDefaultAsync(
            profile => profile.InvitationToken == invitationToken,
            cancellationToken);
        if (team is null)
            return new(Failure: TeamProfileFailure.TeamNotFound);
        if (team.MemberIds.Contains(userId))
            return new(Failure: TeamProfileFailure.UserAlreadyMember);
        team.MemberIds = [.. team.MemberIds, userId];
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(Map(team));
    }

    public async Task<TeamProfileMutationResult> RotateInvitationAsync(
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var team = await db.TeamProfiles.SingleOrDefaultAsync(
            profile => profile.Id == teamId,
            cancellationToken);
        if (team is null)
            return new(Failure: TeamProfileFailure.TeamNotFound);
        team.InvitationToken = CreateInvitationToken();
        await db.SaveChangesAsync(cancellationToken);
        return new(Map(team));
    }

    public async Task<TeamProfileFailure?> RemoveMemberAsync(
        Guid teamId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var team = await db.TeamProfiles.SingleOrDefaultAsync(
            profile => profile.Id == teamId,
            cancellationToken);
        if (team is null)
            return TeamProfileFailure.TeamNotFound;
        if (team.CaptainId == targetUserId)
            return TeamProfileFailure.CaptainCannotBeRemoved;
        if (!team.MemberIds.Contains(targetUserId))
            return TeamProfileFailure.MemberNotFound;
        team.MemberIds = team.MemberIds.Where(id => id != targetUserId).ToArray();
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<TeamProfileFailure?> LeaveAsync(
        Guid teamId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var team = await db.TeamProfiles.SingleOrDefaultAsync(
            profile => profile.Id == teamId && profile.MemberIds.Contains(userId),
            cancellationToken);
        if (team is null)
            return TeamProfileFailure.MemberNotFound;
        if (team.CaptainId == userId)
            return TeamProfileFailure.CaptainMustTransfer;
        team.MemberIds = team.MemberIds.Where(id => id != userId).ToArray();
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<TeamProfileFailure?> TransferCaptainAsync(
        Guid teamId,
        Guid actorId,
        Guid newCaptainId,
        CancellationToken cancellationToken)
    {
        var team = await db.TeamProfiles.SingleOrDefaultAsync(
            profile => profile.Id == teamId,
            cancellationToken);
        if (team is null)
            return TeamProfileFailure.TeamNotFound;
        if (team.CaptainId != actorId)
            return TeamProfileFailure.CaptainOnly;
        if (!team.MemberIds.Contains(newCaptainId))
            return TeamProfileFailure.MemberNotFound;
        team.CaptainId = newCaptainId;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<TeamProfileFailure?> SoftDeleteAsync(
        Guid teamId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken)
    {
        var team = await db.TeamProfiles.SingleOrDefaultAsync(
            profile => profile.Id == teamId,
            cancellationToken);
        if (team is null)
            return TeamProfileFailure.TeamNotFound;
        team.DeletedAt = deletedAt;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static TeamProfileView Map(TeamProfile profile) => new(
        profile.Id,
        profile.Name,
        profile.AvatarUrl,
        profile.CaptainId,
        profile.MemberIds,
        profile.InvitationToken,
        profile.CreatedAt);

    private static string CreateInvitationToken()
    {
        const string alphabet =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
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
