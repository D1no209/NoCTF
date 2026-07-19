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
        if (await db.TeamMembers.AnyAsync(x => x.CompetitionId == command.CompetitionId && x.UserId == command.UserId, ct))
            return new(null, "user_already_registered");
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
            return new(null, "team_name_or_membership_conflict");
        }
    }

    public async Task<IReadOnlyList<TeamView>> ListAsync(Guid competitionId, bool includePending, CancellationToken ct) =>
        await db.Teams.AsNoTracking()
            .Where(x => x.CompetitionId == competitionId && !x.Deletion.IsDeleted
                && (includePending || x.RegistrationStatus == TeamRegistrationStatus.Approved))
            .OrderBy(x => x.Name)
            .Select(x => new TeamView(x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId,
                x.RegistrationStatus, x.IsLocked, x.RegisteredAt)).ToListAsync(ct);

    public async Task<bool?> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken ct)
    {
        var exists = await db.Teams.AsNoTracking().AnyAsync(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (!exists) return null;
        var changed = await db.Teams.Where(x => x.Id == teamId && x.CompetitionId == competitionId
                && x.RegistrationStatus == TeamRegistrationStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RegistrationStatus, status), ct);
        return changed == 1;
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

    public async Task<TeamView?> UpdateAsync(UpdateTeamCommand command, CancellationToken ct)
    {
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == command.TeamId && x.CompetitionId == command.CompetitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null || entity.IsLocked) return null;
        var finished = await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == command.CompetitionId && x.Status == CompetitionStatus.Finished, ct);
        if (finished) return null;
        entity.Name = command.Name;
        entity.AvatarUrl = command.AvatarUrl;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return null; }
        return Map(entity);
    }

    public async Task<string?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken ct)
    {
        var entity = await db.Teams.SingleOrDefaultAsync(x => x.Id == teamId && x.CompetitionId == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null) return "team_not_found";
        var status = await db.Competitions.AsNoTracking().Where(x => x.Id == competitionId).Select(x => (CompetitionStatus?)x.Status).SingleOrDefaultAsync(ct);
        if (status is CompetitionStatus.Running or CompetitionStatus.Paused) return "competition_active";
        entity.Deletion.IsDeleted = true;
        entity.Deletion.DeletedAt = deletedAt;
        entity.Deletion.DeletedById = actorId;
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static TeamView Map(Team x) => new(x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId,
        x.RegistrationStatus, x.IsLocked, x.RegisteredAt);
}
