using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionManagementStore(NoCtfDbContext db) : ICompetitionManagementStore
{
    public async Task<CompetitionView> CreateAsync(CreateCompetitionCommand command, CancellationToken ct)
    {
        var id = Guid.CreateVersion7(command.CreatedAt);
        var competition = new Competition
        {
            Id = id, Title = command.Title.Trim(), Description = command.Description?.Trim(), OwnerId = command.OwnerId,
            Mode = command.Mode, StartTime = command.StartTime, EndTime = command.EndTime,
            Status = CompetitionStatus.Draft, TeamRegistrationAutoApprove = command.TeamRegistrationAutoApprove,
            MaxTeamMembers = command.MaxTeamMembers, CreatedAt = command.CreatedAt, UpdatedAt = command.CreatedAt
        };
        db.Competitions.Add(competition);
        db.CompetitionConfigurations.Add(new CompetitionConfiguration
        {
            CompetitionId = id, Mode = command.Mode, Revision = 0, UpdatedAt = command.CreatedAt
        });
        await db.SaveChangesAsync(ct);
        return Map(competition);
    }

    public async Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken ct) =>
        await Query(includeDraft).SingleOrDefaultAsync(x => x.Id == competitionId, ct);

    public async Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken ct) =>
        await Query(includeDraft).OrderByDescending(x => x.StartTime).ToListAsync(ct);

    public async Task<CompetitionView?> UpdateAsync(UpdateCompetitionCommand command, CancellationToken ct)
    {
        var entity = await db.Competitions.SingleOrDefaultAsync(x => x.Id == command.CompetitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null || entity.Status == CompetitionStatus.Finished) return null;
        entity.Title = command.Title.Trim();
        entity.Description = command.Description?.Trim();
        entity.StartTime = command.StartTime;
        entity.EndTime = command.EndTime;
        entity.TeamRegistrationAutoApprove = command.TeamRegistrationAutoApprove;
        entity.MaxTeamMembers = command.MaxTeamMembers;
        entity.UpdatedAt = command.UpdatedAt;
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<bool> SoftDeleteAsync(Guid competitionId, Guid actorId, DateTimeOffset deletedAt, CancellationToken ct)
    {
        var entity = await db.Competitions.SingleOrDefaultAsync(x => x.Id == competitionId && !x.Deletion.IsDeleted, ct);
        if (entity is null || entity.Status is CompetitionStatus.Running or CompetitionStatus.Paused) return false;
        entity.Deletion.IsDeleted = true;
        entity.Deletion.DeletedAt = deletedAt;
        entity.Deletion.DeletedById = actorId;
        entity.UpdatedAt = deletedAt;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<CompetitionView> Query(bool includeDraft) =>
        db.Competitions.AsNoTracking()
            .Where(x => !x.Deletion.IsDeleted && (includeDraft || x.Status != CompetitionStatus.Draft))
            .Select(x => new CompetitionView(x.Id, x.Title, x.Description, x.Mode, x.StartTime, x.EndTime,
                x.Status, x.TeamRegistrationAutoApprove, x.MaxTeamMembers, x.OwnerId));

    private static CompetitionView Map(Competition x) =>
        new(x.Id, x.Title, x.Description, x.Mode, x.StartTime, x.EndTime, x.Status,
            x.TeamRegistrationAutoApprove, x.MaxTeamMembers, x.OwnerId);
}
