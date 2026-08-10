using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Shared;
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
        var subject = ResolveSubject(draft, id);
        var related = ResolveRelated(draft, subject);
        db.CompetitionEvents.Add(new CompetitionEvent
        {
            Id = id,
            CompetitionId = draft.CompetitionId,
            Kind = draft.Kind,
            Level = draft.Level,
            Visibility = draft.Visibility,
            ActorUserId = draft.ActorUserId,
            SubjectType = subject.Type,
            SubjectId = subject.Id,
            RelatedType = related?.Type,
            RelatedId = related?.Id,
            ParentEventId = draft.ParentEventId,
            PayloadJson = draft.PayloadJson ?? JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                competitionStatus = draft.CompetitionStatus,
                leaderboardVisibility = draft.LeaderboardVisibility,
                teamRegistrationStatus = draft.TeamRegistrationStatus,
                gameplayFactKind = draft.GameplayFactKind,
                gameplayFactState = draft.GameplayFactState,
                gameplayFactResult = draft.GameplayFactResult,
                runtimeState = draft.RuntimeState,
                runtimeCleanupResult = draft.RuntimeCleanupResult,
                questionStatus = draft.QuestionStatus,
                runtimeGeneration = draft.RuntimeGeneration,
                hostPort = draft.HostPort,
                relatedUserId = draft.RelatedUserId,
                teamId = draft.TeamId,
                competitionChallengeId = draft.CompetitionChallengeId,
                hintId = draft.HintId,
                runtimeInstanceId = draft.RuntimeInstanceId,
                gameplayFactId = draft.GameplayFactId,
                questionId = draft.QuestionId,
                reason = SanitizeReason(draft.Reason)
            }, ExportJsonOptions),
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
            access.CanAccessGameplayFactValues,
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

    public async Task<GameplayFactValueAccessResult> AccessGameplayFactValueAsync(
        GameplayFactValueAccessCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var access = await ResolveAccessAsync(
            command.CompetitionId,
            command.ActorUserId,
            cancellationToken);
        if (access.State != CompetitionEventReadState.Available)
            return new(access.State);
        if (!access.CanAccessGameplayFactValues)
            return new(CompetitionEventReadState.Forbidden);

        var submission = await db.GameplayFacts
            .Where(item =>
                item.CompetitionId == command.CompetitionId
                && item.Id == command.GameplayFactId
                && item.Value != null)
            .Select(item => new
            {
                item.Id,
                item.TeamId,
                item.CompetitionChallengeId,
                item.Kind,
                Value = item.Value!
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (submission is null)
            return new(CompetitionEventReadState.CompetitionNotFound);

        var safeReason = command.Reason.Replace(
            submission.Value,
            "[REDACTED]",
            StringComparison.Ordinal);
        await RecordAsync(new CompetitionEventDraft(
            command.CompetitionId,
            CompetitionEventKind.ProtectedGameplayFactValueAccessed,
            CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Staff,
            command.AccessedAt,
            ActorUserId: command.ActorUserId,
            TeamId: submission.TeamId,
            CompetitionChallengeId: submission.CompetitionChallengeId,
            GameplayFactId: submission.Id,
            GameplayFactKind: submission.Kind,
            Reason: safeReason), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new(
            CompetitionEventReadState.Available,
            new GameplayFactValueAccessView(
                submission.Id,
                submission.Kind,
                submission.Value,
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
                    && ((item.SubjectType == EntityReferenceKind.Team && item.SubjectId == teamId)
                        || (item.RelatedType == EntityReferenceKind.Team && item.RelatedId == teamId)))
                : query.Where(item =>
                    item.Visibility == CompetitionEventVisibility.Public);
        }
        if (filter.Kind is CompetitionEventKind kind)
            query = query.Where(item => item.Kind == kind);
        if (filter.MinimumLevel is CompetitionEventLevel level)
            query = query.Where(item => item.Level >= level);
        if (filter.TeamId is Guid filteredTeamId)
            query = query.Where(item =>
                item.SubjectType == EntityReferenceKind.Team && item.SubjectId == filteredTeamId
                || item.RelatedType == EntityReferenceKind.Team && item.RelatedId == filteredTeamId);
        if (filter.ActorUserId is Guid actorUserId)
            query = query.Where(item => item.ActorUserId == actorUserId);
        if (filter.CompetitionChallengeId is Guid competitionChallengeId)
            query = query.Where(item =>
                item.SubjectType == EntityReferenceKind.CompetitionChallenge
                    && item.SubjectId == competitionChallengeId
                || item.RelatedType == EntityReferenceKind.CompetitionChallenge
                    && item.RelatedId == competitionChallengeId);
        if (filter.RuntimeInstanceId is Guid runtimeInstanceId)
            query = query.Where(item =>
                item.SubjectType == EntityReferenceKind.RuntimeInstance
                    && item.SubjectId == runtimeInstanceId
                || item.RelatedType == EntityReferenceKind.RuntimeInstance
                    && item.RelatedId == runtimeInstanceId);
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
            .Select(item => item.TeamId)
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Distinct()
            .ToArray();
        var teams = await db.Teams.AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);
        var challengeIds = events
            .Select(item => item.CompetitionChallengeId)
            .Where(item => item is not null)
            .Select(item => item!.Value)
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

        return events.Select(item =>
        {
            var payload = ParseLegacyPayload(item.PayloadJson);
            var relatedUserId = item.RelatedUserId;
            var teamId = item.TeamId;
            var challengeId = item.CompetitionChallengeId;
            return new CompetitionEventView(
            item.Id,
            item.CompetitionId,
            item.Kind,
            item.Level,
            item.Visibility,
            item.ActorUserId,
            Resolve(users, item.ActorUserId),
            relatedUserId,
            Resolve(users, relatedUserId),
            teamId,
            Resolve(teams, teamId),
            challengeId,
            Resolve(challenges, challengeId),
            item.HintId,
            item.RuntimeInstanceId,
            item.GameplayFactId,
            item.QuestionId,
            item.ParentEventId,
            payload.CompetitionStatus,
            payload.LeaderboardVisibility,
            payload.TeamRegistrationStatus,
            payload.GameplayFactKind,
            payload.GameplayFactState,
            payload.GameplayFactResult,
            payload.RuntimeState,
            payload.RuntimeCleanupResult,
            payload.QuestionStatus,
            payload.RuntimeGeneration,
            payload.HostPort,
            payload.Reason,
            item.OccurredAt);
        }).ToArray();
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

    private static (EntityReferenceKind Type, Guid Id) ResolveSubject(
        CompetitionEventDraft draft,
        Guid eventId)
    {
        if (draft.SubjectType is { } explicitType && draft.SubjectId is { } explicitId)
            return (explicitType, explicitId);
        if (draft.GameplayFactId is { } gameplayFactId)
            return (EntityReferenceKind.GameplayFact, gameplayFactId);
        if (draft.RuntimeInstanceId is { } runtimeId)
            return (EntityReferenceKind.RuntimeInstance, runtimeId);
        if (draft.HintId is { } hintId)
            return (EntityReferenceKind.ChallengeHint, hintId);
        if (draft.CompetitionChallengeId is { } competitionChallengeId)
            return (EntityReferenceKind.CompetitionChallenge, competitionChallengeId);
        if (draft.TeamId is { } teamId)
            return (EntityReferenceKind.Team, teamId);
        if (draft.QuestionId is { } notificationId)
            return (EntityReferenceKind.Notification, notificationId);
        return (EntityReferenceKind.Competition, draft.CompetitionId == Guid.Empty ? eventId : draft.CompetitionId);
    }

    private static (EntityReferenceKind Type, Guid Id)? ResolveRelated(
        CompetitionEventDraft draft,
        (EntityReferenceKind Type, Guid Id) subject)
    {
        if ((draft.RelatedType is null) != (draft.RelatedId is null))
            throw new InvalidOperationException(
                "Competition event related type and id must be supplied together.");
        if (draft.RelatedType is { } explicitType && draft.RelatedId is { } explicitId)
            return (explicitType, explicitId);

        return Candidate(EntityReferenceKind.User, draft.RelatedUserId, subject)
            ?? Candidate(EntityReferenceKind.Team, draft.TeamId, subject)
            ?? Candidate(
                EntityReferenceKind.CompetitionChallenge,
                draft.CompetitionChallengeId,
                subject)
            ?? Candidate(EntityReferenceKind.GameplayFact, draft.GameplayFactId, subject)
            ?? Candidate(EntityReferenceKind.RuntimeInstance, draft.RuntimeInstanceId, subject)
            ?? Candidate(EntityReferenceKind.ChallengeHint, draft.HintId, subject)
            ?? Candidate(EntityReferenceKind.Notification, draft.QuestionId, subject);
    }

    private static (EntityReferenceKind Type, Guid Id)? Candidate(
        EntityReferenceKind type,
        Guid? id,
        (EntityReferenceKind Type, Guid Id) subject) =>
        id is Guid value && (subject.Type != type || subject.Id != value)
            ? (type, value)
            : null;

    private static LegacyEventPayload ParseLegacyPayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LegacyEventPayload>(json, ExportJsonOptions) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private sealed record LegacyEventPayload(
        CompetitionStatus? CompetitionStatus = null,
        CompetitionLeaderboardVisibility? LeaderboardVisibility = null,
        TeamRegistrationStatus? TeamRegistrationStatus = null,
        NoCTF.Domain.Gameplay.GameplayFactKind? GameplayFactKind = null,
        NoCTF.Domain.Gameplay.GameplayFactState? GameplayFactState = null,
        NoCTF.Domain.Gameplay.GameplayFactResult? GameplayFactResult = null,
        NoCTF.Domain.Runtime.RuntimeState? RuntimeState = null,
        NoCTF.Domain.Runtime.RuntimeCleanupResult? RuntimeCleanupResult = null,
        NoCTF.Domain.Challenges.Questions.CompetitionQuestionStatus? QuestionStatus = null,
        int? RuntimeGeneration = null,
        int? HostPort = null,
        string? Reason = null);

    private sealed record AccessResolution(
        CompetitionEventReadState State,
        CompetitionEventAccessLevel? AccessLevel = null,
        Guid? TeamId = null,
        bool CanExport = false,
        bool CanAccessGameplayFactValues = false);
}
