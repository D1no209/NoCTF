using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Observability;
using NoCTF.Infrastructure.Persistence;
using NoCTF.GameModes.Awdp.Scoring;

namespace NoCTF.Infrastructure.Competitions.Events;

public sealed class CompetitionEventStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox)
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
        var entity = CompetitionEventGeneratedCatalog.Create(draft.Kind);
        entity.Id = id;
        entity.CompetitionId = draft.CompetitionId;
        entity.Level = draft.Level;
        entity.Visibility = draft.Visibility;
        entity.ActorUserId = draft.ActorUserId;
        entity.SubjectType = subject.Type;
        entity.SubjectId = subject.Id;
        entity.RelatedType = related?.Type;
        entity.RelatedId = related?.Id;
        entity.ParentEventId = draft.ParentEventId;
        entity.OccurredAt = draft.OccurredAt;
        entity.RelatedUserId = draft.RelatedUserId;
        entity.TeamId = draft.TeamId;
        entity.CompetitionChallengeId = draft.CompetitionChallengeId;
        entity.HintId = draft.HintId;
        entity.RuntimeInstanceId = draft.RuntimeInstanceId;
        entity.GameplayFactId = draft.GameplayFactId;
        entity.QuestionId = draft.QuestionId;
        entity.CompetitionStatus = draft.CompetitionStatus;
        entity.PreviousCompetitionStatus = draft.PreviousCompetitionStatus;
        entity.LeaderboardVisibility = draft.LeaderboardVisibility;
        entity.PreviousLeaderboardVisibility = draft.PreviousLeaderboardVisibility;
        entity.CompetitionAccessMode = draft.CompetitionAccessMode;
        entity.PreviousCompetitionAccessMode = draft.PreviousCompetitionAccessMode;
        entity.CompetitionAudienceChangeKind = draft.CompetitionAudienceChangeKind;
        entity.TeamRegistrationStatus = draft.TeamRegistrationStatus;
        entity.GameplayFactKind = draft.GameplayFactKind;
        entity.GameplayFactState = draft.GameplayFactState;
        entity.GameplayFactResult = draft.GameplayFactResult;
        entity.RuntimeState = draft.RuntimeState;
        entity.RuntimeCleanupResult = draft.RuntimeCleanupResult;
        entity.QuestionStatus = draft.QuestionStatus;
        entity.HostPort = draft.HostPort;
        entity.Reason = SanitizeReason(draft.Reason);
        entity.TrackKey = draft.TrackKey;
        entity.PreviousTrackKey = draft.PreviousTrackKey;
        entity.Automatic = draft.Automatic;
        entity.PatchUploadId = draft.PatchUploadId;
        entity.AwdpFixOutcome = draft.AwdpFixOutcome;
        entity.GameplayFactFailureCode = draft.GameplayFactFailureCode;
        entity.ResolvedAt = draft.ResolvedAt;
        entity.TrackConfigurationEnabled = draft.TrackConfigurationEnabled;
        entity.DefaultTrackKey = draft.DefaultTrackKey;
        entity.ReassignedTeamCount = draft.ReassignedTeamCount;
        entity.TrackKeys = (draft.TrackKeys ?? []).Select((value, position) =>
            new CompetitionEventTrackKey
            {
                Id = Guid.CreateVersion7(),
                Position = position,
                Value = value
            }).ToList();
        entity.IncludesProtectedFlags = draft.IncludesProtectedFlags;
        entity.FrozenStartAt = draft.FrozenStartAt;
        entity.HiddenStartAt = draft.HiddenStartAt;
        if (draft.TrafficCapture is { } traffic)
        {
            entity.TrafficSegmentId = traffic.SegmentId;
            entity.TrafficBindingIndex = traffic.BindingIndex;
            entity.TrafficConnectionId = traffic.ConnectionId;
            entity.TrafficStartedAt = traffic.StartedAt;
            entity.TrafficEndedAt = traffic.EndedAt;
            entity.TrafficClientAddress = traffic.ClientAddress;
            entity.TrafficClientPort = traffic.ClientPort;
            entity.TrafficDestinationAddress = traffic.DestinationAddress;
            entity.TrafficDestinationPort = traffic.DestinationPort;
            entity.TrafficClientToRuntimeBytes = traffic.ClientToRuntimeBytes;
            entity.TrafficRuntimeToClientBytes = traffic.RuntimeToClientBytes;
            entity.TrafficCapturedBytes = traffic.CapturedBytes;
            entity.TrafficTruncated = traffic.Truncated;
        }
        db.CompetitionEvents.Add(entity);
        await outbox.PublishAsync(new CompetitionEventCommitted(
            draft.CompetitionId,
            id,
            draft.Kind,
            draft.Level,
            draft.OccurredAt,
            draft.CompetitionAudienceChangeKind,
            draft.CompetitionAccessMode));
        return id;
    }

    public async Task<CompetitionEventPage> QueryAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(
            query.CompetitionId,
            query.UserId,
            allowArchivedStaff: true,
            cancellationToken);
        if (access.State != CompetitionEventReadState.Available)
            return new(access.State);
        if (query.From is null
            && access.AccessLevel != CompetitionEventAccessLevel.Staff)
            return new(CompetitionEventReadState.Forbidden);

        var (items, total) = await LoadAsync(query, access, cancellationToken);
        return new(
            CompetitionEventReadState.Available,
            access.AccessLevel,
            access.TeamId,
            access.CanExport,
            access.CanAccessGameplayFactValues,
            await EnrichAsync(items, cancellationToken),
            total);
    }

    public async Task<CompetitionEventExportResult> ExportAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(
            query.CompetitionId,
            query.UserId,
            allowArchivedStaff: false,
            cancellationToken);
        if (access.State != CompetitionEventReadState.Available)
            return new(access.State);
        if (!access.CanExport)
            return new(CompetitionEventReadState.Forbidden);
        if (query.From is null || query.To is null)
            return new(CompetitionEventReadState.InvalidQuery);

        var items = await EnrichAsync(
            (await LoadAsync(query, access, cancellationToken)).Items,
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
            $"competition-{query.CompetitionId:N}-{query.From.Value:yyyyMMdd}-{query.To.Value:yyyyMMdd}.jsonl";
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
            allowArchivedStaff: true,
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

        if (access.ShouldAuditGameplayFactValueAccess)
        {
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
                GameplayFactKind: submission.Kind), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        if (access.ShouldAuditGameplayFactValueAccess)
            await outbox.FlushCommittedMessagesAsync();
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
        bool allowArchivedStaff,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(item =>
                item.Id == userId
                && item.AccountStatus == UserAccountStatus.Active)
            .Select(item => new { item.Role })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
            return new(CompetitionEventReadState.Forbidden);

        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == competitionId)
            .Select(item => new
            {
                item.Status,
                item.DeletedAt,
                item.OwnerId,
                IsManager = item.Collaborators.Any(collaborator =>
                    collaborator.Role == CompetitionCollaboratorRole.Manager
                    && collaborator.UserId == userId),
                IsJudge = item.Collaborators.Any(collaborator =>
                    collaborator.Role == CompetitionCollaboratorRole.Judge
                    && collaborator.UserId == userId),
                IsObserver = item.Collaborators.Any(collaborator =>
                    collaborator.Role == CompetitionCollaboratorRole.Observer
                    && collaborator.UserId == userId)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return new(CompetitionEventReadState.CompetitionNotFound);

        var administrator = user.Role == UserRole.Administrator;
        var owner = competition.OwnerId == userId;
        var manager = competition.IsManager;
        var judge = competition.IsJudge;
        var observer = competition.IsObserver;
        var staff = administrator || owner || manager || judge || observer;
        if (competition.DeletedAt is not null && (!allowArchivedStaff || !staff))
            return new(CompetitionEventReadState.CompetitionNotFound);
        if (staff)
        {
            return new(
                CompetitionEventReadState.Available,
                CompetitionEventAccessLevel.Staff,
                null,
                administrator || owner || manager,
                administrator || owner || manager || judge,
                !administrator && (owner || manager || judge));
        }
        if (competition.Status == CompetitionStatus.Draft)
            return new(CompetitionEventReadState.Forbidden);

        var teamId = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && team.Members.Any(member => member.UserId == userId))
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

    private async Task<(IReadOnlyList<CompetitionEvent> Items, int Total)> LoadAsync(
        CompetitionEventQuery filter,
        AccessResolution access,
        CancellationToken cancellationToken)
    {
        var query = db.CompetitionEvents.AsNoTracking()
            .Where(item => item.CompetitionId == filter.CompetitionId);
        if (filter.From is DateTimeOffset from && filter.To is DateTimeOffset to)
        {
            query = query.Where(item =>
                item.OccurredAt >= from
                && item.OccurredAt <= to);
        }
        if (access.AccessLevel != CompetitionEventAccessLevel.Staff)
        {
            var competition = await db.Competitions.AsNoTracking()
                .Where(item => item.Id == filter.CompetitionId)
                .Select(item => new { item.Mode, item.TracksEnabled, item.Tracks })
                .SingleAsync(cancellationToken);
            var internalTrackKeys = CompetitionTrackConfiguration.EffectiveFor(
                    competition.Mode,
                    competition.TracksEnabled,
                    competition.Tracks)
                .Tracks.Where(track => track.IsInternal)
                .Select(track => track.Key)
                .ToArray();
            query = access.TeamId is Guid teamId
                ? query.Where(item =>
                    item.Visibility == CompetitionEventVisibility.Public
                    || item.Visibility == CompetitionEventVisibility.Team
                    && ((item.SubjectType == EntityReferenceKind.Team && item.SubjectId == teamId)
                        || (item.RelatedType == EntityReferenceKind.Team && item.RelatedId == teamId)))
                : query.Where(item =>
                    item.Visibility == CompetitionEventVisibility.Public);
            if (internalTrackKeys.Length > 0)
            {
                query = query.Where(item =>
                    !db.Teams.Any(team =>
                        team.CompetitionId == filter.CompetitionId
                        && internalTrackKeys.Contains(team.TrackKey)
                        && ((item.SubjectType == EntityReferenceKind.Team && item.SubjectId == team.Id)
                            || (item.RelatedType == EntityReferenceKind.Team && item.RelatedId == team.Id))));
            }
        }
        if (filter.Kind is CompetitionEventKind kind)
            query = query.Where(item => item.Kind == kind);
        else if (filter.Kinds is { Count: > 0 } kinds)
            query = query.Where(item => kinds.Contains(item.Kind));
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
        if (filter.OffsetMode)
        {
            var total = await query.CountAsync(cancellationToken);
            var ordered = filter.Desc
                ? query.OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id)
                : query.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id);
            return (await ordered.Skip(filter.Offset).Take(filter.Limit).ToListAsync(cancellationToken), total);
        }
        if (filter.BeforeOccurredAt is DateTimeOffset beforeOccurredAt
            && filter.BeforeId is Guid beforeId)
        {
            query = query.Where(item =>
                item.OccurredAt < beforeOccurredAt
                || item.OccurredAt == beforeOccurredAt
                && item.Id.CompareTo(beforeId) < 0);
        }
        return (await query
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(filter.Limit)
            .ToListAsync(cancellationToken), 0);
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
        var users = userIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(user => userIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.UserName, cancellationToken);
        var teamIds = events
            .Select(item => item.TeamId)
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Distinct()
            .ToArray();
        var teams = teamIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Teams.AsNoTracking()
                .Where(team => teamIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);
        var challengeIds = events
            .Select(item => item.CompetitionChallengeId)
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Distinct()
            .ToArray();
        var challenges = challengeIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challengeIds.Contains(challenge.Id))
                .Join(
                    db.Challenges.AsNoTracking(),
                    competitionChallenge => competitionChallenge.ChallengeId,
                    challenge => challenge.Id,
                    (competitionChallenge, challenge) => new
                    {
                        competitionChallenge.Id,
                        Title = competitionChallenge.CustomTitle ?? challenge.Title
                    })
                .ToDictionaryAsync(
                    item => item.Id,
                    item => item.Title,
                    cancellationToken);

        return events.Select(item =>
        {
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
            item.CompetitionStatus,
            item.LeaderboardVisibility,
            item.CompetitionAccessMode,
            item.PreviousCompetitionAccessMode,
            item.CompetitionAudienceChangeKind,
            item.TeamRegistrationStatus,
            item.GameplayFactKind,
            item.GameplayFactState,
            item.GameplayFactResult,
            item.RuntimeState,
            item.RuntimeCleanupResult,
            item.QuestionStatus,
            item.HostPort,
            item.Reason,
            item.TrackKey,
            item.PreviousTrackKey,
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

    private sealed record AccessResolution(
        CompetitionEventReadState State,
        CompetitionEventAccessLevel? AccessLevel = null,
        Guid? TeamId = null,
        bool CanExport = false,
        bool CanAccessGameplayFactValues = false,
        bool ShouldAuditGameplayFactValueAccess = false);
}
