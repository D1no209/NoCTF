using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;

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

    private static TeamView Map(Team x) => new(x.Id, x.CompetitionId, x.Name, x.AvatarUrl, x.CaptainId,
        x.RegistrationStatus, x.IsLocked, x.RegisteredAt);
}
