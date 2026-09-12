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
    ICompetitionEventRecorder? eventRecorder = null,
    CompetitionReadModelCache? readModels = null,
    AggregatePatchPostCommitActions? postCommitActions = null) : ICompetitionManagementStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new NoOpTransactionalMessageOutbox();

    public async Task<CompetitionCreationResult> CreateAsync(
        CreateCompetitionCommand command,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
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
            AccessMode = command.AccessMode,
            Mode = command.Mode,
            StartAt = command.StartTime,
            EndAt = command.EndTime,
            Status = CompetitionStatus.Draft,
            TeamRegistrationAutoApprove = command.TeamRegistrationAutoApprove,
            AllowTeamRegistrationWhileRunning = command.AllowTeamRegistrationWhileRunning,
            MaxTeamMembers = command.MaxTeamMembers,
            MaxConcurrentRuntimeInstancesPerTeam = command.MaxConcurrentRuntimeInstancesPerTeam,
            MaxActiveQuestionsPerTeam = command.MaxActiveQuestionsPerTeam,
            MaxParticipantMessagesBeforeHandlerReply =
                command.MaxParticipantMessagesBeforeHandlerReply,
            AllowChallengeOwnersToHandleQuestions =
                command.AllowChallengeOwnersToHandleQuestions,
            PracticeModeEnabled = command.PracticeModeEnabled,
            TracksEnabled = command.TracksEnabled,
            CreatedAt = command.CreatedAt,
            UpdatedAt = command.CreatedAt,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(command.Mode),
            TrackConfigurationJson = CompetitionTrackConfiguration.Serialize(
                CompetitionTrackConfiguration.DefaultFor(command.Mode)),
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
            CompetitionStatus: CompetitionStatus.Draft,
            CompetitionAccessMode: command.AccessMode), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await InvalidateReadModelsAsync(competition.Id, CancellationToken.None);
        await outbox.FlushOutgoingMessagesAsync();
        return new(CompetitionCreationState.Created, Map(competition));
    }

    public Task<CompetitionView?> FindAsync(
        Guid competitionId,
        bool includeDraft,
        CancellationToken ct) =>
        includeDraft || readModels is null
            ? LoadAsync(competitionId, includeDraft, ct)
            : readModels.GetAsync(
                competitionId,
                token => LoadAsync(competitionId, includeDraft: false, token),
                ct);

    public async Task<IReadOnlyList<CompetitionView>> ListAsync(
        bool includeDraft,
        CancellationToken ct) =>
        includeDraft || readModels is null
            ? await LoadListAsync(includeDraft, ct)
            : await readModels.ListAsync(
                token => LoadListAsync(includeDraft: false, token),
                ct);

    public async Task<CompetitionView?> UpdateAsync(
        UpdateCompetitionCommand command,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM competitions WHERE id = {command.CompetitionId} FOR UPDATE",
            ct);
        var competition = await db.Competitions
            .SingleOrDefaultAsync(x => x.Id == command.CompetitionId
                && x.DeletedAt == null, ct);
        if (competition is null)
            return null;
        var previousAccessMode = competition.AccessMode;
        if (competition.PracticeModeEnabled
            && !command.PracticeModeEnabled
            && await db.RuntimeInstances.AnyAsync(runtime =>
                runtime.CompetitionId == competition.Id
                && runtime.Purpose == NoCTF.Domain.Runtime.RuntimePurpose.Practice
                && (runtime.State == NoCTF.Domain.Runtime.RuntimeState.Queued
                    || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Provisioning
                    || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Running
                    || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Stopping), ct))
        {
            return null;
        }
        competition.Title = command.Title.Trim();
        competition.Description = command.Description?.Trim();
        competition.StartAt = command.StartTime;
        competition.EndAt = command.EndTime;
        competition.TeamRegistrationAutoApprove = command.TeamRegistrationAutoApprove;
        competition.AllowTeamRegistrationWhileRunning = command.AllowTeamRegistrationWhileRunning;
        competition.MaxTeamMembers = command.MaxTeamMembers;
        competition.MaxConcurrentRuntimeInstancesPerTeam =
            command.MaxConcurrentRuntimeInstancesPerTeam;
        competition.MaxActiveQuestionsPerTeam = command.MaxActiveQuestionsPerTeam;
        competition.MaxParticipantMessagesBeforeHandlerReply =
            command.MaxParticipantMessagesBeforeHandlerReply;
        competition.AllowChallengeOwnersToHandleQuestions =
            command.AllowChallengeOwnersToHandleQuestions;
        competition.PracticeModeEnabled = command.PracticeModeEnabled;
        competition.WriteUpSubmissionRequired = command.WriteUpSubmissionRequired;
        competition.WriteUpSubmissionDeadlineHours = command.WriteUpSubmissionDeadlineHours;
        competition.AccessMode = command.AccessMode;
        competition.UpdatedAt = command.UpdatedAt;
        await events.RecordAsync(new(
            competition.Id,
            CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.UpdatedAt,
            ActorUserId: command.ActorId,
            CompetitionStatus: competition.Status), ct);
        if (competition.AccessMode != previousAccessMode)
        {
            await events.RecordAsync(new(
                competition.Id,
                CompetitionEventKind.CompetitionAudienceChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Staff,
                command.UpdatedAt,
                ActorUserId: command.ActorId,
                CompetitionStatus: competition.Status,
                CompetitionAccessMode: competition.AccessMode,
                PreviousCompetitionAccessMode: previousAccessMode,
                CompetitionAudienceChangeKind: CompetitionAudienceChangeKind.AccessMode), ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await InvalidateReadModelsAsync(competition.Id, CancellationToken.None);
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
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
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
        await InvalidateReadModelsAsync(competition.Id, CancellationToken.None);
        await outbox.FlushOutgoingMessagesAsync();
        return true;
    }

    private Task<CompetitionView?> LoadAsync(
        Guid competitionId,
        bool includeDraft,
        CancellationToken ct) =>
        Project(EntityQuery(includeDraft).Where(competition => competition.Id == competitionId))
            .SingleOrDefaultAsync(ct);

    private Task<CompetitionView[]> LoadListAsync(bool includeDraft, CancellationToken ct) =>
        Project(EntityQuery(includeDraft).OrderByDescending(competition => competition.StartAt))
            .ToArrayAsync(ct);

    private IQueryable<Competition> EntityQuery(bool includeDraft) =>
        db.Competitions.AsNoTracking()
            .Where(x => x.DeletedAt == null && (includeDraft || x.Status != CompetitionStatus.Draft));

    private static IQueryable<CompetitionView> Project(IQueryable<Competition> query) =>
        query.Select(x => new CompetitionView(x.Id, x.Title, x.Description, x.Mode, x.StartAt, x.EndAt,
            x.Status, x.TeamRegistrationAutoApprove, x.MaxTeamMembers,
            x.MaxConcurrentRuntimeInstancesPerTeam, x.OwnerId,
            x.FrozenStartAt, x.HiddenStartAt,
            x.AllowTeamRegistrationWhileRunning,
            x.DeletedAt,
            x.MaxActiveQuestionsPerTeam,
            x.MaxParticipantMessagesBeforeHandlerReply,
            x.AllowChallengeOwnersToHandleQuestions,
            x.PracticeModeEnabled,
            x.PosterFileId,
            x.TracksEnabled,
            x.AccessMode,
            x.WriteUpSubmissionRequired,
            x.WriteUpSubmissionDeadlineHours));

    private static CompetitionView Map(Competition x) =>
        new(x.Id, x.Title, x.Description, x.Mode, x.StartAt, x.EndAt, x.Status,
            x.TeamRegistrationAutoApprove, x.MaxTeamMembers,
            x.MaxConcurrentRuntimeInstancesPerTeam, x.OwnerId,
            x.FrozenStartAt, x.HiddenStartAt,
            x.AllowTeamRegistrationWhileRunning,
            x.DeletedAt,
            x.MaxActiveQuestionsPerTeam,
            x.MaxParticipantMessagesBeforeHandlerReply,
            x.AllowChallengeOwnersToHandleQuestions,
            x.PracticeModeEnabled,
            x.PosterFileId,
            x.TracksEnabled,
            x.AccessMode,
            x.WriteUpSubmissionRequired,
            x.WriteUpSubmissionDeadlineHours);

    private Task InvalidateReadModelsAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        if (readModels is null)
            return Task.CompletedTask;
        return postCommitActions?.RunOrDeferAsync(
                token => readModels.InvalidateAsync(competitionId, token),
                cancellationToken)
            ?? readModels.InvalidateAsync(competitionId, cancellationToken);
    }
}
