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
            .Select(x => new TeamRegistrationPolicy(x.Status, x.TeamRegistrationAutoApprove, x.Deletion.IsDeleted))
            .SingleOrDefaultAsync(ct);

    public async Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var competitionStatus = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (competitionStatus is null) return new(null, TeamRegistrationFailure.CompetitionNotFound);
        if (competitionStatus is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return new(null, TeamRegistrationFailure.RegistrationClosed);
        if (await db.TeamMembers.AnyAsync(x => x.CompetitionId == command.CompetitionId && x.UserId == command.UserId, ct))
            return new(null, TeamRegistrationFailure.UserAlreadyRegistered);
        var id = Guid.CreateVersion7(command.RegisteredAt);
        var team = new Team
        {
            Id = id, CompetitionId = command.CompetitionId, Name = command.Name, AvatarUrl = command.AvatarUrl,
            CaptainId = command.UserId, RegistrationStatus = status, RegisteredAt = command.RegisteredAt
        };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember
        {
            Id = Guid.CreateVersion7(command.RegisteredAt), CompetitionId = command.CompetitionId,
            TeamId = id, UserId = command.UserId, Role = TeamMemberRole.Captain, JoinedAt = command.RegisteredAt
        });
        try
        {
            await db.SaveChangesAsync(ct);
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
            .Where(x => x.CompetitionId == competitionId && !x.Deletion.IsDeleted
                && (includePending || x.RegistrationStatus == TeamRegistrationStatus.Approved))
            .OrderBy(x => x.Name)
            .Select(x => new TeamView(x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId,
                x.RegistrationStatus, x.IsLocked, x.RegisteredAt)).ToListAsync(ct);

    public async Task<TeamReviewStoreResult> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competitionStatus = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (competitionStatus is null) return new(false, TeamRegistrationFailure.CompetitionNotFound);
        if (competitionStatus == CompetitionStatus.Finished) return new(false, TeamRegistrationFailure.CompetitionFinished);
        var exists = await db.Teams.AsNoTracking().AnyAsync(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (!exists) return new(false, TeamRegistrationFailure.TeamNotFound);
        var changed = await db.Teams.Where(x => x.Id == teamId && x.CompetitionId == competitionId
                && x.RegistrationStatus == TeamRegistrationStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RegistrationStatus, status), ct);
        await transaction.CommitAsync(ct);
        return changed == 1 ? new(true) : new(false, TeamRegistrationFailure.TeamReviewConflict);
    }

    public Task<TeamView?> FindAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken ct) =>
        db.Teams.AsNoTracking().Where(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted
                && (includePending || x.RegistrationStatus == TeamRegistrationStatus.Approved))
            .Select(x => new TeamView(x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId,
                x.RegistrationStatus, x.IsLocked, x.RegisteredAt)).SingleOrDefaultAsync(ct);

    public Task<TeamView?> FindForUserAsync(Guid competitionId, Guid userId, bool includePending, CancellationToken ct) =>
        (from team in db.Teams.AsNoTracking()
         join member in db.TeamMembers.AsNoTracking() on team.Id equals member.TeamId
         where team.CompetitionId == competitionId && member.UserId == userId
               && !team.Deletion.IsDeleted
               && (includePending || team.RegistrationStatus == TeamRegistrationStatus.Approved)
         select new TeamView(team.Id, team.CompetitionId, team.Name, team.AvatarUrl, team.CaptainId,
             team.RegistrationStatus, team.IsLocked, team.RegisteredAt)).SingleOrDefaultAsync(ct);

    public async Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken ct) =>
        await db.Teams.AsNoTracking().AnyAsync(x => x.Id == teamId && x.CompetitionId == competitionId
            && !x.Deletion.IsDeleted && x.CaptainId == actorId, ct)
        || await db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId && x.Role == UserRole.Administrator, ct)
        || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId && x.OwnerId == actorId && !x.Deletion.IsDeleted, ct)
        || await db.CompetitionCollaborators.AsNoTracking().AnyAsync(x => x.CompetitionId == competitionId
            && x.UserId == actorId && x.Role == CompetitionCollaboratorRole.Manager, ct);

    public async Task<TeamUpdateStoreResult> UpdateAsync(UpdateTeamCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status is null) return new(null, TeamRegistrationFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished) return new(null, TeamRegistrationFailure.CompetitionFinished);
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == command.TeamId && x.CompetitionId == command.CompetitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return new(null, TeamRegistrationFailure.TeamNotFound);
        if (entity.IsLocked) return new(null, TeamRegistrationFailure.TeamLocked);
        entity.Name = command.Name;
        entity.AvatarUrl = command.AvatarUrl;
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
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
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return TeamRegistrationFailure.TeamNotFound;
        entity.Deletion.IsDeleted = true;
        entity.Deletion.DeletedAt = deletedAt;
        entity.Deletion.DeletedById = actorId;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    private static TeamView Map(Team x) => new(x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId,
        x.RegistrationStatus, x.IsLocked, x.RegisteredAt);
}
