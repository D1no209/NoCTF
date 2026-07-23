using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfTeamRegistrationStore(NoCtfDbContext db) : ITeamRegistrationStore
{
    public Task<TeamRegistrationPolicy?> GetPolicyAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().Where(x => x.Id == competitionId)
            .Select(x => new TeamRegistrationPolicy(x.Status, x.TeamRegistrationAutoApprove, x.DeletedAt != null))
            .SingleOrDefaultAsync(ct);

    public async Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var competitionStatus = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (competitionStatus is null) return new(null, TeamRegistrationFailure.CompetitionNotFound);
        if (competitionStatus is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return new(null, TeamRegistrationFailure.RegistrationClosed);
        if (await db.Teams.AnyAsync(x => x.CompetitionId == command.CompetitionId
            && x.DeletedAt == null
            && x.MemberIds.Contains(command.UserId), ct))
            return new(null, TeamRegistrationFailure.UserAlreadyRegistered);
        var id = Guid.CreateVersion7(command.RegisteredAt);
        var team = new Team
        {
            Id = id,
            CompetitionId = command.CompetitionId,
            Name = command.Name,
            NormalizedName = command.Name.ToUpperInvariant(),
            AvatarUrl = command.AvatarUrl,
            CaptainId = command.UserId,
            MemberIds = [command.UserId],
            InvitationToken = CreateInvitationToken(),
            RegistrationStatus = status,
            RegisteredAt = command.RegisteredAt
        };
        db.Teams.Add(team);
        try
        {
            await db.SaveChangesAsync(ct);
            await LeaderboardRevision.IncrementAsync(db, command.CompetitionId, ct);
            await transaction.CommitAsync(ct);
            return new(Map(team), null);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            return new(null, TeamRegistrationFailure.TeamNameOrMembershipConflict);
        }
    }

    public async Task<IReadOnlyList<TeamView>> ListAsync(Guid competitionId, bool includePending, CancellationToken ct) =>
        await db.Teams.AsNoTracking()
            .Where(x => x.CompetitionId == competitionId && x.DeletedAt == null
                && (includePending || x.RegistrationStatus == TeamRegistrationStatus.Approved))
            .OrderBy(x => x.Name)
            .Select(x => new TeamView(x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId, x.MemberIds,
                x.RegistrationStatus, x.IsLocked, x.RegisteredAt)).ToListAsync(ct);

    public async Task<TeamReviewStoreResult> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competitionStatus = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (competitionStatus is null) return new(false, TeamRegistrationFailure.CompetitionNotFound);
        if (competitionStatus == CompetitionStatus.Finished) return new(false, TeamRegistrationFailure.CompetitionFinished);
        var exists = await db.Teams.AsNoTracking().AnyAsync(x => x.Id == teamId
            && x.CompetitionId == competitionId && x.DeletedAt == null, ct);
        if (!exists) return new(false, TeamRegistrationFailure.TeamNotFound);
        var changed = await db.Teams.Where(x => x.Id == teamId && x.CompetitionId == competitionId
                && x.RegistrationStatus == TeamRegistrationStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RegistrationStatus, status), ct);
        if (changed == 1)
            await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
        return changed == 1 ? new(true) : new(false, TeamRegistrationFailure.TeamReviewConflict);
    }

    public async Task<TeamReviewStoreResult> ResubmitAsync(
        Guid competitionId,
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null)
            return new(false, TeamRegistrationFailure.CompetitionNotFound);
        if (status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
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
            await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
        return changed == 1
            ? new(true)
            : new(false, TeamRegistrationFailure.TeamReviewConflict);
    }

    public Task<TeamView?> FindAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken ct) =>
        db.Teams.AsNoTracking().Where(x => x.Id == teamId && x.CompetitionId == competitionId && x.DeletedAt == null
                && (includePending || x.RegistrationStatus == TeamRegistrationStatus.Approved))
            .Select(x => new TeamView(x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId, x.MemberIds,
                x.RegistrationStatus, x.IsLocked, x.RegisteredAt)).SingleOrDefaultAsync(ct);

    public Task<TeamView?> FindForUserAsync(Guid competitionId, Guid userId, bool includePending, CancellationToken ct) =>
        db.Teams.AsNoTracking().Where(team => team.CompetitionId == competitionId
                && team.MemberIds.Contains(userId)
                && team.DeletedAt == null
                && (includePending || team.RegistrationStatus == TeamRegistrationStatus.Approved))
            .Select(team => new TeamView(team.Id, team.CompetitionId, team.Name, team.AvatarUrl,
                team.CaptainId, team.MemberIds, team.RegistrationStatus, team.IsLocked, team.RegisteredAt))
            .SingleOrDefaultAsync(ct);

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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status is null) return new(null, TeamRegistrationFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished) return new(null, TeamRegistrationFailure.CompetitionFinished);
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == command.TeamId
            && x.CompetitionId == command.CompetitionId && x.DeletedAt == null, ct);
        if (entity is null) return new(null, TeamRegistrationFailure.TeamNotFound);
        if (entity.IsLocked) return new(null, TeamRegistrationFailure.TeamLocked);
        entity.Name = command.Name;
        entity.NormalizedName = command.Name.ToUpperInvariant();
        entity.AvatarUrl = command.AvatarUrl;
        try
        {
            await db.SaveChangesAsync(ct);
            await LeaderboardRevision.IncrementAsync(db, command.CompetitionId, ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException) { return new(null, TeamRegistrationFailure.TeamConflict); }
        return new(Map(entity));
    }

    public async Task<TeamRegistrationFailure?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return TeamRegistrationFailure.CompetitionNotFound;
        if (status is CompetitionStatus.Running or CompetitionStatus.Paused) return TeamRegistrationFailure.CompetitionActive;
        if (status == CompetitionStatus.Finished) return TeamRegistrationFailure.CompetitionFinished;
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == teamId
            && x.CompetitionId == competitionId && x.DeletedAt == null, ct);
        if (entity is null) return TeamRegistrationFailure.TeamNotFound;
        entity.DeletedAt = deletedAt;
        await db.SaveChangesAsync(ct);
        await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    private static TeamView Map(Team x) => new(
        x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId, x.MemberIds,
        x.RegistrationStatus, x.IsLocked, x.RegisteredAt);

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
