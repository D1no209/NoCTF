using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Observability;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Events;

public sealed class CompetitionEventStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox)
    : ICompetitionEventStore, ICompetitionEventRecorder
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask<Guid> RecordAsync(
        CompetitionEventDraft draft,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.CreateVersion7(draft.OccurredAt);
        db.CompetitionEvents.Add(new CompetitionEvent
        {
            Id = id,
            CompetitionId = draft.CompetitionId,
            Kind = draft.Kind,
            Level = draft.Level,
            Visibility = draft.Visibility,
            ActorUserId = draft.ActorUserId,
            RelatedUserId = draft.RelatedUserId,
            TeamId = draft.TeamId,
            CompetitionChallengeId = draft.CompetitionChallengeId,
            HintId = draft.HintId,
            RuntimeInstanceId = draft.RuntimeInstanceId,
            SubmissionId = draft.SubmissionId,
            ScoringEventId = draft.ScoringEventId,
            QuestionId = draft.QuestionId,
            ParentEventId = draft.ParentEventId,
            CompetitionStatus = draft.CompetitionStatus,
            LeaderboardVisibility = draft.LeaderboardVisibility,
            TeamRegistrationStatus = draft.TeamRegistrationStatus,
            SubmissionKind = draft.SubmissionKind,
            SubmissionState = draft.SubmissionState,
            ScoringEventKind = draft.ScoringEventKind,
            ScoringResult = draft.ScoringResult,
            RuntimeState = draft.RuntimeState,
            QuestionStatus = draft.QuestionStatus,
            RuntimeGeneration = draft.RuntimeGeneration,
            HostPort = draft.HostPort,
            Reason = SanitizeReason(draft.Reason),
            OccurredAt = draft.OccurredAt
        });
        await outbox.PublishAsync(new CompetitionEventCommitted(
            draft.CompetitionId,
            id,
            draft.Kind,
            draft.Level,
            draft.OccurredAt));
        return id;
    }

    public async Task<CompetitionEventPage> QueryAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(
            query.CompetitionId,
            query.UserId,
            cancellationToken);
        if (access.State != CompetitionEventReadState.Available)
            return new(access.State);

        var items = await LoadAsync(query, access, cancellationToken);
        return new(
            CompetitionEventReadState.Available,
            access.AccessLevel,
            access.TeamId,
            access.CanExport,
            access.CanAccessSubmissionFlags,
            await EnrichAsync(items, cancellationToken));
    }

    public async Task<CompetitionEventExportResult> ExportAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(
            query.CompetitionId,
            query.UserId,
            cancellationToken);
        if (access.State != CompetitionEventReadState.Available)
            return new(access.State);
        if (!access.CanExport)
            return new(CompetitionEventReadState.Forbidden);

        var items = await EnrichAsync(
            await LoadAsync(query, access, cancellationToken),
            cancellationToken);
        var stream = new MemoryStream();
        foreach (var item in items)
        {
            await JsonSerializer.SerializeAsync(
                stream,
                item,
                ExportJsonOptions,
                cancellationToken);
            stream.WriteByte((byte)'\n');
        }
        stream.Position = 0;
        var fileName =
            $"competition-{query.CompetitionId:N}-{query.From:yyyyMMdd}-{query.To:yyyyMMdd}.jsonl";
        return new(
            CompetitionEventReadState.Available,
            new CompetitionEventExport(stream, fileName));
    }

    public async Task<SubmissionFlagAccessResult> AccessSubmissionFlagAsync(
        SubmissionFlagAccessCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var access = await ResolveAccessAsync(
            command.CompetitionId,
            command.ActorUserId,
            cancellationToken);
        if (access.State != CompetitionEventReadState.Available)
            return new(access.State);
        if (!access.CanAccessSubmissionFlags)
            return new(CompetitionEventReadState.Forbidden);

        var submission = await db.Submissions
            .Where(item =>
                item.CompetitionId == command.CompetitionId
                && item.Id == command.SubmissionId
                && item.SubmittedFlag != null)
            .Select(item => new
            {
                item.Id,
                item.TeamId,
                item.CompetitionChallengeId,
                item.Kind,
                SubmittedFlag = item.SubmittedFlag!
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (submission is null)
            return new(CompetitionEventReadState.CompetitionNotFound);

        var safeReason = command.Reason.Replace(
            submission.SubmittedFlag,
            "[REDACTED]",
            StringComparison.Ordinal);
        await RecordAsync(new CompetitionEventDraft(
            command.CompetitionId,
            CompetitionEventKind.ProtectedSubmissionFlagAccessed,
            CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Staff,
            command.AccessedAt,
            ActorUserId: command.ActorUserId,
            TeamId: submission.TeamId,
            CompetitionChallengeId: submission.CompetitionChallengeId,
            SubmissionId: submission.Id,
            SubmissionKind: submission.Kind,
            Reason: safeReason), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new(
            CompetitionEventReadState.Available,
            new SubmissionFlagAccessView(
                submission.Id,
                submission.Kind,
                submission.SubmittedFlag,
                command.AccessedAt));
    }

    private async Task<AccessResolution> ResolveAccessAsync(
        Guid competitionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(item =>
                item.Id == userId
                && item.Kind == UserKind.Human
                && item.AccountStatus == UserAccountStatus.Active)
            .Select(item => new { item.Role })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
            return new(CompetitionEventReadState.Forbidden);

        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.Status,
                item.OwnerId,
                item.ManagerIds,
                item.JudgeIds,
                item.ObserverIds
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return new(CompetitionEventReadState.CompetitionNotFound);

        var administrator = user.Role == UserRole.Administrator;
        var owner = competition.OwnerId == userId;
        var manager = competition.ManagerIds.Contains(userId);
        var judge = competition.JudgeIds.Contains(userId);
        var observer = competition.ObserverIds.Contains(userId);
        if (administrator || owner || manager || judge || observer)
        {
            return new(
                CompetitionEventReadState.Available,
                CompetitionEventAccessLevel.Staff,
                null,
                administrator || owner || manager,
                administrator || owner || manager || judge);
        }
        if (competition.Status == CompetitionStatus.Draft)
            return new(CompetitionEventReadState.Forbidden);

        var teamId = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && team.MemberIds.Contains(userId))
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return teamId is Guid approvedTeamId
            ? new(
                CompetitionEventReadState.Available,
                CompetitionEventAccessLevel.Team,
                approvedTeamId,
                false,
                false)
            : new(
                CompetitionEventReadState.Available,
                CompetitionEventAccessLevel.Participant,
                null,
                false,
                false);
    }

    private async Task<IReadOnlyList<CompetitionEvent>> LoadAsync(
        CompetitionEventQuery filter,
        AccessResolution access,
        CancellationToken cancellationToken)
    {
        var query = db.CompetitionEvents.AsNoTracking()
            .Where(item =>
                item.CompetitionId == filter.CompetitionId
                && item.OccurredAt >= filter.From
                && item.OccurredAt <= filter.To);
        if (access.AccessLevel != CompetitionEventAccessLevel.Staff)
        {
            query = access.TeamId is Guid teamId
                ? query.Where(item =>
                    item.Visibility == CompetitionEventVisibility.Public
                    || item.Visibility == CompetitionEventVisibility.Team
                    && item.TeamId == teamId)
                : query.Where(item =>
                    item.Visibility == CompetitionEventVisibility.Public);
        }
        if (filter.Kind is CompetitionEventKind kind)
            query = query.Where(item => item.Kind == kind);
        if (filter.MinimumLevel is CompetitionEventLevel level)
            query = query.Where(item => item.Level >= level);
        if (filter.TeamId is Guid filteredTeamId)
            query = query.Where(item => item.TeamId == filteredTeamId);
        if (filter.ActorUserId is Guid actorUserId)
            query = query.Where(item => item.ActorUserId == actorUserId);
        if (filter.CompetitionChallengeId is Guid competitionChallengeId)
            query = query.Where(item =>
                item.CompetitionChallengeId == competitionChallengeId);
        if (filter.RuntimeInstanceId is Guid runtimeInstanceId)
            query = query.Where(item => item.RuntimeInstanceId == runtimeInstanceId);
        if (filter.BeforeOccurredAt is DateTimeOffset beforeOccurredAt
            && filter.BeforeId is Guid beforeId)
        {
            query = query.Where(item =>
                item.OccurredAt < beforeOccurredAt
                || item.OccurredAt == beforeOccurredAt
                && item.Id.CompareTo(beforeId) < 0);
        }
        return await query
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(filter.Limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CompetitionEventView>> EnrichAsync(
        IReadOnlyList<CompetitionEvent> events,
        CancellationToken cancellationToken)
    {
        var userIds = events
            .SelectMany(item => new[] { item.ActorUserId, item.RelatedUserId })
            .Where(item => item != null)
            .Select(item => item!.Value)
            .Distinct()
            .ToArray();
        var users = await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, cancellationToken);
        var teamIds = events
            .Where(item => item.TeamId != null)
            .Select(item => item.TeamId!.Value)
            .Distinct()
            .ToArray();
        var teams = await db.Teams.AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);
        var challengeIds = events
            .Where(item => item.CompetitionChallengeId != null)
            .Select(item => item.CompetitionChallengeId!.Value)
            .Distinct()
            .ToArray();
        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challengeIds.Contains(challenge.Id))
            .Join(
                db.Challenges.AsNoTracking(),
                competitionChallenge => competitionChallenge.ChallengeId,
                challenge => challenge.Id,
                (competitionChallenge, challenge) => new
                {
                    competitionChallenge.Id,
                    challenge.Title
                })
            .ToDictionaryAsync(
                item => item.Id,
                item => item.Title,
                cancellationToken);

        return events.Select(item => new CompetitionEventView(
            item.Id,
            item.CompetitionId,
            item.Kind,
            item.Level,
            item.Visibility,
            item.ActorUserId,
            Resolve(users, item.ActorUserId),
            item.RelatedUserId,
            Resolve(users, item.RelatedUserId),
            item.TeamId,
            Resolve(teams, item.TeamId),
            item.CompetitionChallengeId,
            Resolve(challenges, item.CompetitionChallengeId),
            item.HintId,
            item.RuntimeInstanceId,
            item.SubmissionId,
            item.ScoringEventId,
            item.QuestionId,
            item.ParentEventId,
            item.CompetitionStatus,
            item.LeaderboardVisibility,
            item.TeamRegistrationStatus,
            item.SubmissionKind,
            item.SubmissionState,
            item.ScoringEventKind,
            item.ScoringResult,
            item.RuntimeState,
            item.QuestionStatus,
            item.RuntimeGeneration,
            item.HostPort,
            item.Reason,
            item.OccurredAt)).ToArray();
    }

    private static string? Resolve(
        IReadOnlyDictionary<Guid, string> values,
        Guid? id) =>
        id is Guid value && values.TryGetValue(value, out var displayName)
            ? displayName
            : null;

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;
        var sanitized = PlatformLogRedactor.Redact(reason.Trim(), []);
        return sanitized.Length <= 512 ? sanitized : sanitized[..512];
    }

    private sealed record AccessResolution(
        CompetitionEventReadState State,
        CompetitionEventAccessLevel? AccessLevel = null,
        Guid? TeamId = null,
        bool CanExport = false,
        bool CanAccessSubmissionFlags = false);
}
