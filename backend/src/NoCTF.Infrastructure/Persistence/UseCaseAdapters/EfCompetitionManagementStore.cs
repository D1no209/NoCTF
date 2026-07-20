using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

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
            CompetitionId = id, Mode = command.Mode,
            Json = GameModeDefaultConfiguration.GetCompetitionJson(command.Mode),
            Revision = 0, UpdatedAt = command.CreatedAt
        });
        await db.SaveChangesAsync(ct);
        return Map(competition);
    }

    public async Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken ct) =>
        await Query(includeDraft).SingleOrDefaultAsync(x => x.Id == competitionId, ct);

    public async Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken ct) =>
        await Query(includeDraft).OrderByDescending(x => x.StartTime).ToListAsync(ct);

    public async Task<CompetitionView?> UpdateAsync(
        UpdateCompetitionCommand command,
        CompetitionStatus expectedStatus,
        CancellationToken ct)
    {
        var affected = await db.Competitions
            .Where(x => x.Id == command.CompetitionId
                && !x.Deletion.IsDeleted
                && x.Status == expectedStatus
                && x.Status != CompetitionStatus.Finished)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Title, command.Title.Trim())
                .SetProperty(x => x.Description, command.Description == null ? null : command.Description.Trim())
                .SetProperty(x => x.StartTime, command.StartTime)
                .SetProperty(x => x.EndTime, command.EndTime)
                .SetProperty(x => x.TeamRegistrationAutoApprove, command.TeamRegistrationAutoApprove)
                .SetProperty(x => x.MaxTeamMembers, command.MaxTeamMembers)
                .SetProperty(x => x.UpdatedAt, command.UpdatedAt), ct);
        return affected == 1 ? await FindAsync(command.CompetitionId, true, ct) : null;
    }

    public async Task<bool> SoftDeleteAsync(
        Guid competitionId,
        CompetitionStatus expectedStatus,
        Guid actorId,
        DateTimeOffset deletedAt,
        CancellationToken ct)
    {
        var affected = await db.Competitions
            .Where(x => x.Id == competitionId
                && !x.Deletion.IsDeleted
                && x.Status == expectedStatus
                && x.Status != CompetitionStatus.Running
                && x.Status != CompetitionStatus.Paused
                && x.Status != CompetitionStatus.Finished)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Deletion.IsDeleted, true)
                .SetProperty(x => x.Deletion.DeletedAt, deletedAt)
                .SetProperty(x => x.Deletion.DeletedById, actorId)
                .SetProperty(x => x.UpdatedAt, deletedAt), ct);
        return affected == 1;
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
