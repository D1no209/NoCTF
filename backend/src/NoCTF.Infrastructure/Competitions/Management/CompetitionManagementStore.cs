using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Administration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Infrastructure.Competitions.Management;

public sealed class CompetitionManagementStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox? messageOutbox = null,
    ICompetitionEventRecorder? eventRecorder = null) : ICompetitionManagementStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new OpenApiTransactionalMessageOutbox();

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
            MaxConcurrentRuntimeInstancesPerTeam = command.MaxConcurrentRuntimeInstancesPerTeam,
            CreatedAt = command.CreatedAt,
            UpdatedAt = command.CreatedAt,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(command.Mode),
            ConfigurationRevision = 0,
            ConfigurationUpdatedAt = command.CreatedAt,
            FlagDerivationSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
        };
        db.Competitions.Add(competition);
        await events.RecordAsync(new(
            competition.Id,
            CompetitionEventKind.CompetitionCreated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.CreatedAt,
            ActorUserId: command.OwnerId,
            CompetitionStatus: CompetitionStatus.Draft), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(CompetitionCreationState.Created, Map(competition));
    }

    public async Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken ct) =>
        await Project(EntityQuery(includeDraft).Where(x => x.Id == competitionId))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken ct) =>
        await Project(EntityQuery(includeDraft).OrderByDescending(x => x.StartAt)).ToListAsync(ct);

    public async Task<CompetitionView?> UpdateAsync(
        UpdateCompetitionCommand command,
        CompetitionStatus expectedStatus,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competition = await db.Competitions
            .SingleOrDefaultAsync(x => x.Id == command.CompetitionId
                && x.DeletedAt == null
                && x.Status == expectedStatus, ct);
        if (competition is null)
            return null;
        competition.Title = command.Title.Trim();
        competition.Description = command.Description?.Trim();
        competition.StartAt = command.StartTime;
        competition.EndAt = command.EndTime;
        competition.TeamRegistrationAutoApprove = command.TeamRegistrationAutoApprove;
        competition.MaxTeamMembers = command.MaxTeamMembers;
        competition.MaxConcurrentRuntimeInstancesPerTeam =
            command.MaxConcurrentRuntimeInstancesPerTeam;
        competition.UpdatedAt = command.UpdatedAt;
        await events.RecordAsync(new(
            competition.Id,
            CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.UpdatedAt,
            CompetitionStatus: competition.Status), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return Map(competition);
    }

    public async Task<bool> SoftDeleteAsync(
        Guid competitionId,
        CompetitionStatus expectedStatus,
        Guid actorId,
        DateTimeOffset deletedAt,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competition = await db.Competitions
            .SingleOrDefaultAsync(x => x.Id == competitionId
                && x.DeletedAt == null
                && x.Status == expectedStatus
                && x.Status != CompetitionStatus.Running
                && x.Status != CompetitionStatus.Paused
                && x.Status != CompetitionStatus.Finished, ct);
        if (competition is null)
            return false;
        competition.DeletedAt = deletedAt;
        competition.UpdatedAt = deletedAt;
        await events.RecordAsync(new(
            competition.Id,
            CompetitionEventKind.CompetitionDeleted,
            CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Staff,
            deletedAt,
            ActorUserId: actorId,
            CompetitionStatus: competition.Status), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return true;
    }

    private IQueryable<Competition> EntityQuery(bool includeDraft) =>
        db.Competitions.AsNoTracking()
            .Where(x => x.DeletedAt == null && (includeDraft || x.Status != CompetitionStatus.Draft));

    private static IQueryable<CompetitionView> Project(IQueryable<Competition> query) =>
        query.Select(x => new CompetitionView(x.Id, x.Title, x.Description, x.Mode, x.StartAt, x.EndAt,
            x.Status, x.TeamRegistrationAutoApprove, x.MaxTeamMembers,
            x.MaxConcurrentRuntimeInstancesPerTeam, x.OwnerId,
            x.LeaderboardVisibility, x.LeaderboardVisibilityStartsAt));

    private static CompetitionView Map(Competition x) =>
        new(x.Id, x.Title, x.Description, x.Mode, x.StartAt, x.EndAt, x.Status,
            x.TeamRegistrationAutoApprove, x.MaxTeamMembers,
            x.MaxConcurrentRuntimeInstancesPerTeam, x.OwnerId,
            x.LeaderboardVisibility, x.LeaderboardVisibilityStartsAt);
}
