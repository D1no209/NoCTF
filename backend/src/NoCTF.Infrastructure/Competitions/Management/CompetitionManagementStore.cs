using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Administration;

namespace NoCTF.Infrastructure.Competitions.Management;

public sealed class CompetitionManagementStore(NoCtfDbContext db) : ICompetitionManagementStore
{
    public async Task<CompetitionCreationResult> CreateAsync(
        CreateCompetitionCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            [command.OwnerId],
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                CompetitionCreationState.UserNotFound,
                UserIds: eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                CompetitionCreationState.RoleNotEligible,
                UserIds: eligibility.RoleIneligibleUserIds);
        }

        var id = Guid.CreateVersion7(command.CreatedAt);
        var competition = new Competition
        {
            Id = id,
            Title = command.Title.Trim(),
            Description = command.Description?.Trim(),
            OwnerId = command.OwnerId,
            Mode = command.Mode,
            StartAt = command.StartTime,
            EndAt = command.EndTime,
            Status = CompetitionStatus.Draft,
            TeamRegistrationAutoApprove = command.TeamRegistrationAutoApprove,
            MaxTeamMembers = command.MaxTeamMembers,
            CreatedAt = command.CreatedAt,
            UpdatedAt = command.CreatedAt,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(command.Mode),
            ConfigurationRevision = 0,
            ConfigurationUpdatedAt = command.CreatedAt,
            FlagDerivationSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
        };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(CompetitionCreationState.Created, Map(competition));
    }

    public async Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken ct) =>
        await Project(EntityQuery(includeDraft).Where(x => x.Id == competitionId))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken ct) =>
        await Project(EntityQuery(includeDraft)).OrderByDescending(x => x.StartTime).ToListAsync(ct);

    public async Task<CompetitionView?> UpdateAsync(
        UpdateCompetitionCommand command,
        CompetitionStatus expectedStatus,
        CancellationToken ct)
    {
        var affected = await db.Competitions
            .Where(x => x.Id == command.CompetitionId
                && x.DeletedAt == null
                && x.Status == expectedStatus)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Title, command.Title.Trim())
                .SetProperty(x => x.Description, command.Description == null ? null : command.Description.Trim())
                .SetProperty(x => x.StartAt, command.StartTime)
                .SetProperty(x => x.EndAt, command.EndTime)
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
                && x.DeletedAt == null
                && x.Status == expectedStatus
                && x.Status != CompetitionStatus.Running
                && x.Status != CompetitionStatus.Paused
                && x.Status != CompetitionStatus.Finished)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.DeletedAt, deletedAt)
                .SetProperty(x => x.UpdatedAt, deletedAt), ct);
        return affected == 1;
    }

    private IQueryable<Competition> EntityQuery(bool includeDraft) =>
        db.Competitions.AsNoTracking()
            .Where(x => x.DeletedAt == null && (includeDraft || x.Status != CompetitionStatus.Draft));

    private static IQueryable<CompetitionView> Project(IQueryable<Competition> query) =>
        query.Select(x => new CompetitionView(x.Id, x.Title, x.Description, x.Mode, x.StartAt, x.EndAt,
            x.Status, x.TeamRegistrationAutoApprove, x.MaxTeamMembers, x.OwnerId));

    private static CompetitionView Map(Competition x) =>
        new(x.Id, x.Title, x.Description, x.Mode, x.StartAt, x.EndAt, x.Status,
            x.TeamRegistrationAutoApprove, x.MaxTeamMembers, x.OwnerId);
}
