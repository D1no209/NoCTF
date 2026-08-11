using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Questions;

public sealed class CompetitionQuestionStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events,
    ILogger<CompetitionQuestionStore> logger) : ICompetitionQuestionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<CompetitionQuestionMutationResult> CreateAsync(
        CreateCompetitionQuestionCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        var user = await db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == command.ActorUserId)
            .Select(candidate => new { candidate.Kind, candidate.AccountStatus })
            .SingleOrDefaultAsync(ct);
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == command.CompetitionId)
            .Select(candidate => new
            {
                candidate.Status,
                candidate.MaxActiveQuestionsPerTeam
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return Failure(
                CompetitionQuestionFailure.NotFound,
                command.CompetitionId,
                null,
                null,
                command.ActorUserId);

        var team = await db.Teams.AsNoTracking()
            .Where(candidate => candidate.CompetitionId == command.CompetitionId
                && candidate.MemberIds.Contains(command.ActorUserId)
                && candidate.DeletedAt == null)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.RegistrationStatus,
                candidate.IsBanned
            })
            .SingleOrDefaultAsync(ct);
        var hasApprovedTeam = team is not null
            && team.RegistrationStatus == TeamRegistrationStatus.Approved
            && !team.IsBanned;
        var challengeReference = command.CompetitionChallengeId is null
            ? CompetitionQuestionChallengeReferenceState.Missing
            : await db.CompetitionChallenges.AsNoTracking().AnyAsync(candidate =>
                candidate.Id == command.CompetitionChallengeId
                && candidate.CompetitionId == command.CompetitionId
                && candidate.IsPublished
                && candidate.DeletedAt == null, ct)
                ? CompetitionQuestionChallengeReferenceState.Valid
                : CompetitionQuestionChallengeReferenceState.Invalid;
        var hasValidSubmission = command.GameplayFactId is null
            || team is not null && await db.GameplayFacts.AsNoTracking().AnyAsync(submission =>
                submission.Id == command.GameplayFactId
                && submission.CompetitionId == command.CompetitionId
                && submission.TeamId == team.Id
                && (command.Subject == CompetitionQuestionSubject.Platform
                    || submission.CompetitionChallengeId == command.CompetitionChallengeId), ct);
        var activeQuestionCount = 0;
        if (hasApprovedTeam)
        {
            await AcquireTeamQuestionLockAsync(team!.Id, ct);
            activeQuestionCount = await CountActiveQuestionsAsync(
                command.CompetitionId,
                team.Id,
                ct);
        }
        var context = new CompetitionQuestionCreationContext(
            user is { Kind: UserKind.Human, AccountStatus: UserAccountStatus.Active },
            competition.Status,
            hasApprovedTeam,
            command.Subject,
            challengeReference,
            hasValidSubmission,
            activeQuestionCount,
            competition.MaxActiveQuestionsPerTeam);
        if (CompetitionQuestionRules.ValidateCreation(context) is { } contextFailure)
            return Failure(
                contextFailure,
                command.CompetitionId,
                null,
                team?.Id,
                command.ActorUserId,
                contextFailure == CompetitionQuestionFailure.TeamActiveQuestionLimitReached
                    ? competition.MaxActiveQuestionsPerTeam
                    : null);

        var duplicateCutoff = command.Now.AddMinutes(-1);
        var recent = await db.Notifications.AsNoTracking()
            .Where(notification => notification.Kind == NotificationKind.QuestionOpened
                && notification.SourceType == NotificationSourceType.User
                && notification.SourceId == command.ActorUserId
                && notification.RelatedType == EntityReferenceKind.Competition
                && notification.RelatedId == command.CompetitionId
                && notification.SentAt >= duplicateCutoff)
            .Select(notification => notification.ContentJson)
            .ToArrayAsync(ct);
        if (recent.Select(ParseRoot).Any(root =>
                root is not null && root.Title == command.Title && root.Body == command.Body))
            return Failure(
                CompetitionQuestionFailure.SpamRejected,
                command.CompetitionId,
                null,
                team?.Id,
                command.ActorUserId);

        var root = new QuestionRootPayload(
            1,
            command.Subject,
            command.Title,
            command.Body,
            team!.Id,
            command.CompetitionChallengeId,
            command.GameplayFactId,
            CompetitionQuestionStatus.Pending);
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(command.Now),
            SourceType = NotificationSourceType.User,
            SourceId = command.ActorUserId,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = command.CompetitionId,
            Kind = NotificationKind.QuestionOpened,
            ContentJson = JsonSerializer.Serialize(root, JsonOptions),
            SentAt = command.Now,
            RelatedType = EntityReferenceKind.Competition,
            RelatedId = command.CompetitionId
        };
        db.Notifications.Add(notification);
        await events.RecordAsync(new(
            command.CompetitionId,
            CompetitionEventKind.QuestionOpened,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.Now,
            ActorUserId: command.ActorUserId,
            TeamId: team.Id,
            CompetitionChallengeId: command.CompetitionChallengeId,
            QuestionId: notification.Id,
            QuestionStatus: CompetitionQuestionStatus.Pending), ct);
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            command.CompetitionId,
            notification.Id,
            null,
            await ResolveHandlerRecipientIdsAsync(
                command.CompetitionId,
                command.CompetitionChallengeId,
                ct),
            NotificationKind.QuestionOpened,
            CompetitionQuestionNotificationEvent.Opened,
            root.Title,
            command.Now,
            0));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(await BuildViewAsync(
            new QuestionAggregate(notification, root, []),
            CompetitionQuestionAccess.Asker,
            true,
            ct));
    }

    public async Task<IReadOnlyList<CompetitionQuestionView>> ListAsync(
        CompetitionQuestionQuery query,
        CancellationToken ct)
    {
        var actor = await ResolveActorContextAsync(query.CompetitionId, query.ActorUserId, ct);
        if (actor is null)
            return [];
        var ownedChallengeIds = await ResolveOwnedCompetitionChallengeIdsAsync(
            query.CompetitionId,
            query.ActorUserId,
            actor,
            ct);
        var roots = await LoadQuestionRootsAsync(query, actor, ownedChallengeIds, ct);
        var aggregates = await LoadAggregatesAsync(roots, ct);
        var visible = aggregates
            .Select(aggregate => new
            {
                Question = aggregate,
                Resolution = ResolveAccess(aggregate, actor, ownedChallengeIds)
            })
            .Where(input => input.Resolution is not null)
            .Select(input => new QuestionViewInput(
                input.Question,
                input.Resolution!.Value.Access))
            .ToArray();
        return await BuildViewsAsync(visible, false, ct);
    }

    public async Task<CompetitionQuestionView?> FindAsync(
        Guid competitionId,
        Guid questionId,
        Guid actorUserId,
        CancellationToken ct)
    {
        var aggregate = await LoadAggregateAsync(competitionId, questionId, ct);
        var actor = await ResolveActorContextAsync(competitionId, actorUserId, ct);
        if (aggregate is null || actor is null)
            return null;
        var access = await ResolveAccessAsync(aggregate, actorUserId, actor, ct);
        return access is null
            ? null
            : await BuildViewAsync(aggregate, access.Value.Access, true, ct);
    }

    public async Task<CompetitionQuestionMutationResult> AddMessageAsync(
        AddCompetitionQuestionMessageCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        await AcquireQuestionLockAsync(command.QuestionId, ct);
        var aggregate = await LoadAggregateAsync(command.CompetitionId, command.QuestionId, ct);
        var actor = await ResolveActorContextAsync(command.CompetitionId, command.ActorUserId, ct);
        if (aggregate is null)
            return Failure(
                CompetitionQuestionFailure.NotFound,
                command.CompetitionId,
                command.QuestionId,
                null,
                command.ActorUserId);
        var access = actor is null
            ? null
            : await ResolveAccessAsync(aggregate, command.ActorUserId, actor, ct);
        if (access is null
            || access.Value.Access is not (CompetitionQuestionAccess.Asker
                or CompetitionQuestionAccess.Handler))
            return Failure(
                CompetitionQuestionFailure.Forbidden,
                command.CompetitionId,
                command.QuestionId,
                aggregate.Root.TeamId,
                command.ActorUserId);
        if (aggregate.Revision != command.ExpectedRevision)
        {
            var current = await BuildViewAsync(aggregate, access.Value.Access, true, ct);
            WarnFailure(
                CompetitionQuestionFailure.RevisionConflict,
                command.CompetitionId,
                command.QuestionId,
                aggregate.Root.TeamId,
                command.ActorUserId);
            return new(current, CompetitionQuestionFailure.RevisionConflict);
        }

        var actorRole = access.Value.ActorRole;
        var nextStatus = CompetitionQuestionRules.StatusAfterMessage(aggregate.Status, actorRole);
        if (nextStatus is null)
            return Failure(
                aggregate.Status == CompetitionQuestionStatus.Closed
                    ? CompetitionQuestionFailure.QuestionClosed
                    : CompetitionQuestionFailure.InvalidTransition,
                command.CompetitionId,
                command.QuestionId,
                aggregate.Root.TeamId,
                command.ActorUserId);
        if (CompetitionQuestionRules.IsParticipant(actorRole))
        {
            await AcquireTeamQuestionLockAsync(aggregate.Root.TeamId, ct);
            if (aggregate.Status == CompetitionQuestionStatus.Resolved
                && await CountActiveQuestionsAsync(
                    command.CompetitionId,
                    aggregate.Root.TeamId,
                    ct) >= actor!.MaxActiveQuestionsPerTeam)
            {
                return Failure(
                    CompetitionQuestionFailure.TeamActiveQuestionLimitReached,
                    command.CompetitionId,
                    command.QuestionId,
                    aggregate.Root.TeamId,
                    command.ActorUserId,
                    actor.MaxActiveQuestionsPerTeam);
            }
            var participantMessages = CountParticipantMessagesSinceHandlerReply(aggregate);
            if (CompetitionQuestionRules.ValidateParticipantMessageLimit(
                    participantMessages,
                    actor!.MaxParticipantMessagesBeforeHandlerReply) is { } limitFailure)
            {
                return Failure(
                    limitFailure,
                    command.CompetitionId,
                    command.QuestionId,
                    aggregate.Root.TeamId,
                    command.ActorUserId,
                    actor.MaxParticipantMessagesBeforeHandlerReply);
            }
        }
        var payload = new QuestionMessagePayload(
            1,
            command.Body,
            aggregate.Status,
            nextStatus.Value,
            actorRole);
        var node = AppendNode(
            aggregate,
            command.ActorUserId,
            NotificationKind.Message,
            JsonSerializer.Serialize(payload, JsonOptions),
            command.Now);
        db.Notifications.Add(node);
        await events.RecordAsync(new(
            command.CompetitionId,
            CompetitionEventKind.QuestionReplied,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.Now,
            ActorUserId: command.ActorUserId,
            TeamId: aggregate.Root.TeamId,
            CompetitionChallengeId: aggregate.Root.CompetitionChallengeId,
            QuestionId: aggregate.RootNotification.Id,
            QuestionStatus: nextStatus.Value), ct);
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            command.CompetitionId,
            aggregate.RootNotification.Id,
            node.Id,
            CompetitionQuestionRules.IsParticipant(actorRole)
                ? await ResolveHandlerRecipientIdsAsync(
                    command.CompetitionId,
                    aggregate.Root.CompetitionChallengeId,
                    ct)
                : await ResolveTeamRecipientIdsAsync(aggregate.Root.TeamId, ct),
            NotificationKind.Message,
            CompetitionQuestionRules.IsParticipant(actorRole)
                ? CompetitionQuestionNotificationEvent.AskerFollowedUp
                : CompetitionQuestionNotificationEvent.HandlerReplied,
            aggregate.Root.Title,
            command.Now,
            aggregate.Revision + 1));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        aggregate.Nodes.Add(node);
        return new(await BuildViewAsync(aggregate, access.Value.Access, true, ct));
    }

    public async Task<CompetitionQuestionMutationResult> ChangeStatusAsync(
        ChangeCompetitionQuestionStatusCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        await AcquireQuestionLockAsync(command.QuestionId, ct);
        var aggregate = await LoadAggregateAsync(command.CompetitionId, command.QuestionId, ct);
        var actor = await ResolveActorContextAsync(command.CompetitionId, command.ActorUserId, ct);
        if (aggregate is null)
            return Failure(
                CompetitionQuestionFailure.NotFound,
                command.CompetitionId,
                command.QuestionId,
                null,
                command.ActorUserId);
        var access = actor is null
            ? null
            : await ResolveAccessAsync(aggregate, command.ActorUserId, actor, ct);
        if (access is null
            || access.Value.Access is not (CompetitionQuestionAccess.Asker
                or CompetitionQuestionAccess.Handler))
            return Failure(
                CompetitionQuestionFailure.Forbidden,
                command.CompetitionId,
                command.QuestionId,
                aggregate.Root.TeamId,
                command.ActorUserId);
        if (aggregate.Revision != command.ExpectedRevision)
        {
            var current = await BuildViewAsync(aggregate, access.Value.Access, true, ct);
            WarnFailure(
                CompetitionQuestionFailure.RevisionConflict,
                command.CompetitionId,
                command.QuestionId,
                aggregate.Root.TeamId,
                command.ActorUserId);
            return new(current, CompetitionQuestionFailure.RevisionConflict);
        }
        var role = access.Value.ActorRole;
        if (!CompetitionQuestionRules.CanTransition(aggregate.Status, command.Status, role))
            return Failure(
                aggregate.Status == CompetitionQuestionStatus.Closed
                    ? CompetitionQuestionFailure.QuestionClosed
                    : CompetitionQuestionFailure.InvalidTransition,
                command.CompetitionId,
                command.QuestionId,
                aggregate.Root.TeamId,
                command.ActorUserId);
        var payload = new QuestionStatusPayload(1, aggregate.Status, command.Status, role);
        var node = AppendNode(
            aggregate,
            command.ActorUserId,
            NotificationKind.QuestionStatusChanged,
            JsonSerializer.Serialize(payload, JsonOptions),
            command.Now);
        db.Notifications.Add(node);
        await events.RecordAsync(new(
            command.CompetitionId,
            CompetitionEventKind.QuestionStatusChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.Now,
            ActorUserId: command.ActorUserId,
            TeamId: aggregate.Root.TeamId,
            CompetitionChallengeId: aggregate.Root.CompetitionChallengeId,
            QuestionId: aggregate.RootNotification.Id,
            QuestionStatus: command.Status), ct);
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            command.CompetitionId,
            aggregate.RootNotification.Id,
            node.Id,
            CompetitionQuestionRules.IsParticipant(role)
                ? await ResolveHandlerRecipientIdsAsync(
                    command.CompetitionId,
                    aggregate.Root.CompetitionChallengeId,
                    ct)
                : await ResolveTeamRecipientIdsAsync(aggregate.Root.TeamId, ct),
            NotificationKind.QuestionStatusChanged,
            CompetitionQuestionNotificationEvent.StatusChanged,
            aggregate.Root.Title,
            command.Now,
            aggregate.Revision + 1));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        aggregate.Nodes.Add(node);
        return new(await BuildViewAsync(aggregate, access.Value.Access, true, ct));
    }

    private async Task AcquireQuestionLockAsync(Guid questionId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({questionId.ToString()}, 0))",
                ct);
        }
    }

    private async Task AcquireTeamQuestionLockAsync(Guid teamId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({$"question-team:{teamId:N}"}, 0))",
                ct);
        }
    }

    private async Task<Guid[]> ResolveHandlerRecipientIdsAsync(
        Guid competitionId,
        Guid? competitionChallengeId,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == competitionId)
            .Select(candidate => new
            {
                candidate.OwnerId,
                candidate.ManagerIds,
                candidate.JudgeIds,
                candidate.AllowChallengeOwnersToHandleQuestions
            })
            .SingleAsync(ct);
        var candidateIds = competition.ManagerIds
            .Concat(competition.JudgeIds)
            .Append(competition.OwnerId)
            .ToList();
        if (competition.AllowChallengeOwnersToHandleQuestions
            && competitionChallengeId is Guid bindingId)
        {
            var challenge = await db.CompetitionChallenges.AsNoTracking()
                .Where(binding => binding.Id == bindingId
                    && binding.CompetitionId == competitionId)
                .Join(
                    db.Challenges.AsNoTracking(),
                    binding => binding.ChallengeId,
                    challenge => challenge.Id,
                    (_, challenge) => new
                    {
                        challenge.OwnerId,
                        challenge.ManagerIds
                    })
                .SingleOrDefaultAsync(ct);
            if (challenge is not null)
            {
                candidateIds.Add(challenge.OwnerId);
                candidateIds.AddRange(challenge.ManagerIds);
            }
        }

        var administratorIds = await db.Users.AsNoTracking()
            .Where(user => user.Role == UserRole.Administrator
                && user.Kind == UserKind.Human
                && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        candidateIds.AddRange(administratorIds);
        var distinctIds = candidateIds.Distinct().ToArray();
        return await db.Users.AsNoTracking()
            .Where(user => distinctIds.Contains(user.Id)
                && user.Kind == UserKind.Human
                && user.AccountStatus == UserAccountStatus.Active)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
    }

    private async Task<Guid[]> ResolveTeamRecipientIdsAsync(
        Guid teamId,
        CancellationToken ct)
    {
        var memberIds = await db.Teams.AsNoTracking()
            .Where(team => team.Id == teamId && team.DeletedAt == null)
            .Select(team => team.MemberIds)
            .SingleOrDefaultAsync(ct) ?? [];
        return await db.Users.AsNoTracking()
            .Where(user => memberIds.Contains(user.Id)
                && user.Kind == UserKind.Human
                && user.AccountStatus == UserAccountStatus.Active)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
    }

    private static Notification AppendNode(
        QuestionAggregate aggregate,
        Guid actorUserId,
        NotificationKind kind,
        string contentJson,
        DateTimeOffset sentAt)
    {
        var previous = aggregate.Nodes.Count == 0
            ? aggregate.RootNotification.Id
            : aggregate.Nodes[^1].Id;
        return new Notification
        {
            Id = Guid.CreateVersion7(sentAt),
            SourceType = NotificationSourceType.User,
            SourceId = actorUserId,
            TargetType = aggregate.RootNotification.TargetType,
            TargetId = aggregate.RootNotification.TargetId,
            Kind = kind,
            ContentJson = contentJson,
            SentAt = sentAt,
            RelatedType = aggregate.RootNotification.RelatedType,
            RelatedId = aggregate.RootNotification.RelatedId,
            ReplyToId = previous
        };
    }

    private async Task<QuestionAggregate?> LoadAggregateAsync(
        Guid competitionId,
        Guid questionId,
        CancellationToken ct)
    {
        var root = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(notification =>
            notification.Id == questionId
            && notification.Kind == NotificationKind.QuestionOpened
            && notification.TargetId == competitionId, ct);
        var payload = root is null ? null : ParseRoot(root.ContentJson);
        return root is null || payload is null ? null : await LoadAggregateAsync(root, payload, ct);
    }

    private async Task<Notification[]> LoadQuestionRootsAsync(
        CompetitionQuestionQuery query,
        ActorContext actor,
        IReadOnlyCollection<Guid> ownedChallengeIds,
        CancellationToken ct)
    {
        var limit = Math.Clamp(query.Limit, 1, CompetitionQuestionRules.MaximumListLimit);
        if (!db.Database.IsRelational())
        {
            var candidates = await db.Notifications.AsNoTracking()
                .Where(notification => notification.Kind == NotificationKind.QuestionOpened
                    && notification.TargetType == NotificationTargetType.CompetitionCollaborators
                    && notification.TargetId == query.CompetitionId)
                .ToArrayAsync(ct);
            var aggregates = await LoadAggregatesAsync(candidates, ct);
            return aggregates
                .Where(aggregate => MatchesQuery(aggregate, query)
                    && ResolveAccess(aggregate, actor, ownedChallengeIds) is not null)
                .OrderByDescending(aggregate => aggregate.Nodes.Count == 0
                    ? aggregate.RootNotification.SentAt
                    : aggregate.Nodes[^1].SentAt)
                .ThenByDescending(aggregate => aggregate.RootNotification.Id)
                .Take(limit)
                .Select(aggregate => aggregate.RootNotification)
                .ToArray();
        }

        var broadAccess = actor.IsPlatformAdministrator
            || actor.IsCompetitionManager
            || actor.IsJudge
            || actor.IsObserver;
        var teamId = actor.TeamId?.ToString() ?? string.Empty;
        var challengeIds = ownedChallengeIds.Select(id => id.ToString()).ToArray();
        var subject = query.Subject?.ToString() ?? string.Empty;
        var status = query.Status?.ToString() ?? string.Empty;
        var competitionChallengeId = query.CompetitionChallengeId?.ToString() ?? string.Empty;
        return await db.Notifications.FromSqlInterpolated($$"""
            WITH RECURSIVE question_thread AS (
                SELECT root.id AS root_id,
                       root.id AS node_id,
                       root.sent_at AS updated_at,
                       root.content_json ->> 'status' AS status,
                       0 AS depth
                FROM notifications AS root
                WHERE root.kind = {{(short)NotificationKind.QuestionOpened}}
                  AND root.target_type = {{(short)NotificationTargetType.CompetitionCollaborators}}
                  AND root.target_id = {{query.CompetitionId}}
                  AND ({{query.Subject is null}}
                       OR root.content_json ->> 'subject' = {{subject}})
                  AND ({{query.CompetitionChallengeId is null}}
                       OR root.content_json ->> 'competitionChallengeId' = {{competitionChallengeId}})
                  AND ({{broadAccess}}
                       OR root.content_json ->> 'teamId' = {{teamId}}
                       OR root.content_json ->> 'competitionChallengeId' = ANY ({{challengeIds}}))
                UNION ALL
                SELECT parent.root_id,
                       child.id,
                       child.sent_at,
                       COALESCE(child.content_json ->> 'to', parent.status),
                       parent.depth + 1
                FROM notifications AS child
                JOIN question_thread AS parent ON child.reply_to_id = parent.node_id
            ),
            question_head AS (
                SELECT DISTINCT ON (root_id)
                       root_id,
                       updated_at,
                       status
                FROM question_thread
                ORDER BY root_id, depth DESC
            )
            SELECT root.*
            FROM notifications AS root
            JOIN question_head AS head ON head.root_id = root.id
            WHERE ({{query.Status is null}} OR head.status = {{status}})
            ORDER BY head.updated_at DESC, root.id DESC
            LIMIT {{limit}}
            """).AsNoTracking().ToArrayAsync(ct);
    }

    private async Task<QuestionAggregate[]> LoadAggregatesAsync(
        IReadOnlyCollection<Notification> roots,
        CancellationToken ct)
    {
        if (roots.Count == 0)
            return [];
        var rootPayloads = roots
            .Select(root => new { Notification = root, Payload = ParseRoot(root.ContentJson) })
            .Where(item => item.Payload is not null)
            .ToDictionary(item => item.Notification.Id, item => item);
        if (rootPayloads.Count == 0)
            return [];

        Notification[] descendants;
        if (!db.Database.IsRelational())
        {
            var all = await db.Notifications.AsNoTracking().ToArrayAsync(ct);
            var includedIds = rootPayloads.Keys.ToHashSet();
            var pending = true;
            while (pending)
            {
                pending = false;
                foreach (var notification in all)
                {
                    if (notification.ReplyToId is not { } parentId
                        || !includedIds.Contains(parentId)
                        || !includedIds.Add(notification.Id))
                        continue;
                    pending = true;
                }
            }
            descendants = all
                .Where(notification => !rootPayloads.ContainsKey(notification.Id)
                    && includedIds.Contains(notification.Id))
                .ToArray();
        }
        else
        {
            var rootIds = rootPayloads.Keys.ToArray();
            descendants = await db.Notifications.FromSqlInterpolated($$"""
                WITH RECURSIVE thread AS (
                    SELECT child.*
                    FROM notifications AS child
                    WHERE child.reply_to_id = ANY ({{rootIds}})
                    UNION ALL
                    SELECT child.*
                    FROM notifications AS child
                    JOIN thread AS parent ON child.reply_to_id = parent.id
                )
                SELECT * FROM thread
                """).AsNoTracking().ToArrayAsync(ct);
        }

        var rootByNode = rootPayloads.Keys.ToDictionary(id => id, id => id);
        var byId = descendants.ToDictionary(notification => notification.Id);
        Guid ResolveRoot(Notification notification)
        {
            if (rootByNode.TryGetValue(notification.Id, out var knownRoot))
                return knownRoot;
            var path = new List<Guid>();
            var current = notification;
            while (true)
            {
                path.Add(current.Id);
                if (current.ReplyToId is not { } parentId)
                    return Guid.Empty;
                if (rootByNode.TryGetValue(parentId, out knownRoot))
                {
                    foreach (var id in path)
                        rootByNode[id] = knownRoot;
                    return knownRoot;
                }
                if (!byId.TryGetValue(parentId, out var parent))
                    return Guid.Empty;
                current = parent;
            }
        }

        var nodesByRoot = rootPayloads.Keys.ToDictionary(id => id, _ => new List<Notification>());
        foreach (var descendant in descendants)
        {
            var rootId = ResolveRoot(descendant);
            if (nodesByRoot.TryGetValue(rootId, out var nodes))
                nodes.Add(descendant);
        }
        return rootPayloads.Values
            .Select(item => new QuestionAggregate(
                item.Notification,
                item.Payload!,
                nodesByRoot[item.Notification.Id]
                    .OrderBy(node => node.SentAt)
                    .ThenBy(node => node.Id)
                    .ToList()))
            .OrderByDescending(aggregate => aggregate.Nodes.Count == 0
                ? aggregate.RootNotification.SentAt
                : aggregate.Nodes[^1].SentAt)
            .ThenByDescending(aggregate => aggregate.RootNotification.Id)
            .ToArray();
    }

    private async Task<QuestionAggregate> LoadAggregateAsync(
        Notification root,
        QuestionRootPayload payload,
        CancellationToken ct)
    {
        List<Notification> nodes;
        if (!db.Database.IsRelational())
        {
            var all = await db.Notifications.AsNoTracking().ToListAsync(ct);
            var ids = new HashSet<Guid> { root.Id };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var item in all)
                {
                    if (item.ReplyToId is not { } parentId
                        || !ids.Contains(parentId)
                        || !ids.Add(item.Id))
                        continue;
                    changed = true;
                }
            }
            nodes = all.Where(item => item.Id != root.Id && ids.Contains(item.Id))
                .OrderBy(item => item.SentAt)
                .ThenBy(item => item.Id)
                .ToList();
        }
        else
        {
            nodes = await db.Notifications.FromSqlInterpolated($$"""
                WITH RECURSIVE thread AS (
                    SELECT n.* FROM notifications AS n WHERE n.reply_to_id = {{root.Id}}
                    UNION ALL
                    SELECT n.* FROM notifications AS n
                    JOIN thread AS parent ON n.reply_to_id = parent.id
                )
                SELECT * FROM thread
                """).AsNoTracking()
                .OrderBy(notification => notification.SentAt)
                .ThenBy(notification => notification.Id)
                .ToListAsync(ct);
        }
        return new(root, payload, nodes);
    }

    private async Task<int> CountActiveQuestionsAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            var roots = await db.Notifications.AsNoTracking()
                .Where(notification =>
                    notification.Kind == NotificationKind.QuestionOpened
                    && notification.TargetType == NotificationTargetType.CompetitionCollaborators
                    && notification.TargetId == competitionId)
                .ToArrayAsync(ct);
            return (await LoadAggregatesAsync(roots, ct)).Count(aggregate =>
                aggregate.Root.TeamId == teamId
                && aggregate.Status is CompetitionQuestionStatus.Pending
                    or CompetitionQuestionStatus.Replied);
        }

        var active = new[]
        {
            CompetitionQuestionStatus.Pending.ToString(),
            CompetitionQuestionStatus.Replied.ToString()
        };
        var team = teamId.ToString();
        var rootsWithActiveHeads = await db.Notifications.FromSqlInterpolated($$"""
            WITH RECURSIVE question_thread AS (
                SELECT root.id AS root_id,
                       root.id AS node_id,
                       root.content_json ->> 'status' AS status,
                       0 AS depth
                FROM notifications AS root
                WHERE root.kind = {{(short)NotificationKind.QuestionOpened}}
                  AND root.target_type = {{(short)NotificationTargetType.CompetitionCollaborators}}
                  AND root.target_id = {{competitionId}}
                  AND root.content_json ->> 'teamId' = {{team}}
                UNION ALL
                SELECT parent.root_id,
                       child.id,
                       COALESCE(child.content_json ->> 'to', parent.status),
                       parent.depth + 1
                FROM notifications AS child
                JOIN question_thread AS parent ON child.reply_to_id = parent.node_id
            ),
            question_head AS (
                SELECT DISTINCT ON (root_id)
                       root_id,
                       status
                FROM question_thread
                ORDER BY root_id, depth DESC
            )
            SELECT root.*
            FROM notifications AS root
            JOIN question_head AS head ON head.root_id = root.id
            WHERE head.status = ANY ({{active}})
            """).AsNoTracking().ToArrayAsync(ct);
        return rootsWithActiveHeads.Length;
    }

    private static int CountParticipantMessagesSinceHandlerReply(
        QuestionAggregate aggregate)
    {
        var messages = aggregate.Nodes
            .Where(node => node.Kind == NotificationKind.Message)
            .Select(node => JsonSerializer.Deserialize<QuestionMessagePayload>(
                node.ContentJson,
                JsonOptions)!)
            .ToArray();
        var lastHandlerIndex = Array.FindLastIndex(
            messages,
            message => CompetitionQuestionRules.IsHandler(message.ActorRole));
        var participantMessages = messages
            .Skip(lastHandlerIndex + 1)
            .Count(message => CompetitionQuestionRules.IsParticipant(message.ActorRole));
        return lastHandlerIndex < 0 ? participantMessages + 1 : participantMessages;
    }

    private async Task<ActorContext?> ResolveActorContextAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == actorUserId
                && candidate.Kind == UserKind.Human
                && candidate.AccountStatus == UserAccountStatus.Active)
            .Select(candidate => new { candidate.Role })
            .SingleOrDefaultAsync(ct);
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == competitionId)
            .Select(candidate => new
            {
                candidate.OwnerId,
                candidate.ManagerIds,
                candidate.JudgeIds,
                candidate.ObserverIds,
                candidate.MaxActiveQuestionsPerTeam,
                candidate.MaxParticipantMessagesBeforeHandlerReply,
                candidate.AllowChallengeOwnersToHandleQuestions
            })
            .SingleOrDefaultAsync(ct);
        if (user is null || competition is null)
            return null;
        var teamId = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId
                && team.MemberIds.Contains(actorUserId)
                && team.DeletedAt == null
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        return new(
            user.Role == UserRole.Administrator,
            competition.OwnerId == actorUserId
                || competition.ManagerIds.Contains(actorUserId),
            competition.JudgeIds.Contains(actorUserId),
            competition.ObserverIds.Contains(actorUserId),
            teamId,
            competition.MaxActiveQuestionsPerTeam,
            competition.MaxParticipantMessagesBeforeHandlerReply,
            competition.AllowChallengeOwnersToHandleQuestions);
    }

    private async Task<Guid[]> ResolveOwnedCompetitionChallengeIdsAsync(
        Guid competitionId,
        Guid actorUserId,
        ActorContext actor,
        CancellationToken ct)
    {
        if (!actor.AllowChallengeOwnersToHandleQuestions
            || actor.IsPlatformAdministrator
            || actor.IsCompetitionManager
            || actor.IsJudge
            || actor.IsObserver)
            return [];
        return await db.CompetitionChallenges.AsNoTracking()
            .Where(binding => binding.CompetitionId == competitionId)
            .Join(
                db.Challenges.AsNoTracking(),
                binding => binding.ChallengeId,
                challenge => challenge.Id,
                (binding, challenge) => new { binding.Id, challenge.OwnerId, challenge.ManagerIds })
            .Where(item => item.OwnerId == actorUserId
                || item.ManagerIds.Contains(actorUserId))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(ct);
    }

    private static bool MatchesQuery(
        QuestionAggregate aggregate,
        CompetitionQuestionQuery query) =>
        (query.CompetitionChallengeId is null
            || aggregate.Root.CompetitionChallengeId == query.CompetitionChallengeId)
        && (query.Subject is null || aggregate.Root.Subject == query.Subject)
        && (query.Status is null || aggregate.Status == query.Status);

    private static AccessResolution? ResolveAccess(
        QuestionAggregate question,
        ActorContext actor,
        IReadOnlyCollection<Guid> ownedChallengeIds)
    {
        if (actor.IsPlatformAdministrator)
            return new(
                CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.PlatformAdministrator);
        if (actor.IsCompetitionManager)
            return new(
                CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.CompetitionManager);
        if (actor.IsJudge)
            return new(
                CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.Judge);
        if (actor.IsObserver)
            return new(
                CompetitionQuestionAccess.Observer,
                CompetitionQuestionParticipantRole.Handler);
        if (actor.TeamId == question.Root.TeamId)
            return new(
                CompetitionQuestionAccess.Asker,
                CompetitionQuestionParticipantRole.Participant);
        if (actor.AllowChallengeOwnersToHandleQuestions
            && question.Root.CompetitionChallengeId is { } challengeId
            && ownedChallengeIds.Contains(challengeId))
            return new(
                CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.ChallengeOwner);
        return null;
    }

    private async Task<AccessResolution?> ResolveAccessAsync(
        QuestionAggregate question,
        Guid actorUserId,
        ActorContext actor,
        CancellationToken ct)
    {
        if (actor.IsPlatformAdministrator)
            return new(
                CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.PlatformAdministrator);
        if (actor.IsCompetitionManager)
            return new(
                CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.CompetitionManager);
        if (actor.IsJudge)
            return new(
                CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.Judge);
        if (actor.IsObserver)
            return new(
                CompetitionQuestionAccess.Observer,
                CompetitionQuestionParticipantRole.Handler);
        if (actor.TeamId == question.Root.TeamId)
            return new(
                CompetitionQuestionAccess.Asker,
                CompetitionQuestionParticipantRole.Participant);
        if (actor.AllowChallengeOwnersToHandleQuestions
            && question.Root.CompetitionChallengeId is { } challengeId
            && await db.CompetitionChallenges.AsNoTracking().AnyAsync(binding =>
                binding.Id == challengeId
                && binding.CompetitionId == question.RootNotification.TargetId
                && db.Challenges.Any(challenge => challenge.Id == binding.ChallengeId
                    && (challenge.OwnerId == actorUserId
                        || challenge.ManagerIds.Contains(actorUserId))), ct))
            return new(
                CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.ChallengeOwner);
        return null;
    }

    private async Task<CompetitionQuestionView> BuildViewAsync(
        QuestionAggregate question,
        CompetitionQuestionAccess access,
        bool includeEntries,
        CancellationToken ct) =>
        (await BuildViewsAsync([new(question, access)], includeEntries, ct))[0];

    private async Task<CompetitionQuestionView[]> BuildViewsAsync(
        IReadOnlyList<QuestionViewInput> questions,
        bool includeEntries,
        CancellationToken ct)
    {
        if (questions.Count == 0)
            return [];
        var actorIds = questions.SelectMany(input => input.Question.Nodes)
            .Select(node => node.SourceId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Concat(questions.Select(input => input.Question.RootNotification.SourceId!.Value))
            .Distinct()
            .ToArray();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        var teamIds = questions.Select(input => input.Question.Root.TeamId).Distinct().ToArray();
        var teamNames = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, team => team.Name, ct);
        var challengeIds = questions
            .Select(input => input.Question.Root.CompetitionChallengeId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var challengeTitles = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            .Where(binding => challengeIds.Contains(binding.Id))
            .Join(
                db.Challenges.IgnoreQueryFilters().AsNoTracking(),
                binding => binding.ChallengeId,
                challenge => challenge.Id,
                (binding, challenge) => new { binding.Id, challenge.Title })
            .ToDictionaryAsync(item => item.Id, item => item.Title, ct);
        var competitionIds = questions
            .Select(input => input.Question.RootNotification.TargetId)
            .Distinct()
            .ToArray();
        var participantMessageLimits = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Where(competition => competitionIds.Contains(competition.Id))
            .ToDictionaryAsync(
                competition => competition.Id,
                competition => competition.MaxParticipantMessagesBeforeHandlerReply,
                ct);
        return questions.Select(input => BuildView(
            input.Question,
            input.Access,
            includeEntries,
            names,
            teamNames,
            challengeTitles,
            participantMessageLimits[input.Question.RootNotification.TargetId])).ToArray();
    }

    private static CompetitionQuestionView BuildView(
        QuestionAggregate question,
        CompetitionQuestionAccess access,
        bool includeEntries,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, string> teamNames,
        IReadOnlyDictionary<Guid, string> challengeTitles,
        int maximumParticipantMessages)
    {
        var askerId = question.RootNotification.SourceId!.Value;
        var lastNode = question.Nodes.LastOrDefault();
        var lastActorId = lastNode?.SourceId ?? askerId;
        var lastActorRole = lastNode is null
            ? CompetitionQuestionParticipantRole.Participant
            : ReadActorRole(lastNode);
        var remainingParticipantMessages = Math.Max(
            0,
            maximumParticipantMessages
                - CountParticipantMessagesSinceHandlerReply(question));
        var entries = includeEntries
            ? question.Nodes.Select(node => MapEntry(node, names))
                .Prepend(new CompetitionQuestionEntryView(
                    question.RootNotification.Id,
                    CompetitionQuestionEntryKind.Message,
                    CompetitionQuestionParticipantRole.Participant,
                    askerId,
                    names.GetValueOrDefault(askerId, "已删除用户"),
                    question.Root.Body,
                    null,
                    null,
                    null,
                    question.RootNotification.SentAt))
                .ToArray()
            : [];
        return new(
            question.RootNotification.Id,
            question.RootNotification.TargetId,
            question.Root.CompetitionChallengeId,
            question.Root.TeamId,
            askerId,
            names.GetValueOrDefault(askerId, "已删除用户"),
            teamNames.GetValueOrDefault(question.Root.TeamId),
            question.Root.GameplayFactId,
            question.Root.Subject,
            question.Root.CompetitionChallengeId is { } challengeId
                ? challengeTitles.GetValueOrDefault(challengeId)
                : null,
            question.Root.Title,
            question.Root.Body,
            question.Status,
            access,
            names.GetValueOrDefault(lastActorId, "已删除用户"),
            lastActorRole,
            remainingParticipantMessages,
            maximumParticipantMessages,
            question.Revision,
            question.RootNotification.SentAt,
            question.Nodes.Count == 0 ? question.RootNotification.SentAt : question.Nodes[^1].SentAt,
            entries);
    }

    private static CompetitionQuestionEntryView MapEntry(
        Notification node,
        IReadOnlyDictionary<Guid, string> names)
    {
        var actorId = node.SourceId ?? Guid.Empty;
        if (node.Kind == NotificationKind.Message)
        {
            var payload = JsonSerializer.Deserialize<QuestionMessagePayload>(node.ContentJson, JsonOptions)!;
            return new(node.Id, CompetitionQuestionEntryKind.Message, payload.ActorRole,
                node.SourceId, names.GetValueOrDefault(actorId, "已删除用户"), payload.Body,
                payload.From, payload.To, null, node.SentAt);
        }
        var status = JsonSerializer.Deserialize<QuestionStatusPayload>(node.ContentJson, JsonOptions)!;
        return new(node.Id, CompetitionQuestionEntryKind.StatusTransition, status.ActorRole,
            node.SourceId, names.GetValueOrDefault(actorId, "已删除用户"), null,
            status.From, status.To, null, node.SentAt);
    }

    private static CompetitionQuestionParticipantRole ReadActorRole(Notification node) =>
        node.Kind == NotificationKind.Message
            ? JsonSerializer.Deserialize<QuestionMessagePayload>(node.ContentJson, JsonOptions)!.ActorRole
            : JsonSerializer.Deserialize<QuestionStatusPayload>(node.ContentJson, JsonOptions)!.ActorRole;

    private static QuestionRootPayload? ParseRoot(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<QuestionRootPayload>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private CompetitionQuestionMutationResult Failure(
        CompetitionQuestionFailure failure,
        Guid competitionId,
        Guid? questionId,
        Guid? teamId,
        Guid userId,
        int? limit = null)
    {
        WarnFailure(failure, competitionId, questionId, teamId, userId);
        return new(null, failure, limit);
    }

    private void WarnFailure(
        CompetitionQuestionFailure failure,
        Guid competitionId,
        Guid? questionId,
        Guid? teamId,
        Guid userId) =>
        logger.LogWarning(
            "Competition question mutation rejected. failureCode={FailureCode} competitionId={CompetitionId} questionId={QuestionId} teamId={TeamId} userId={UserId}",
            failure,
            competitionId,
            questionId,
            teamId,
            userId);

    private sealed record ActorContext(
        bool IsPlatformAdministrator,
        bool IsCompetitionManager,
        bool IsJudge,
        bool IsObserver,
        Guid? TeamId,
        int MaxActiveQuestionsPerTeam,
        int MaxParticipantMessagesBeforeHandlerReply,
        bool AllowChallengeOwnersToHandleQuestions);

    private readonly record struct AccessResolution(
        CompetitionQuestionAccess Access,
        CompetitionQuestionParticipantRole ActorRole);

    private readonly record struct QuestionViewInput(
        QuestionAggregate Question,
        CompetitionQuestionAccess Access);

    private sealed record QuestionRootPayload(
        int SchemaVersion,
        CompetitionQuestionSubject Subject,
        string Title,
        string Body,
        Guid TeamId,
        Guid? CompetitionChallengeId,
        Guid? GameplayFactId,
        CompetitionQuestionStatus Status);

    private sealed record QuestionMessagePayload(
        int SchemaVersion,
        string Body,
        CompetitionQuestionStatus From,
        CompetitionQuestionStatus To,
        CompetitionQuestionParticipantRole ActorRole);

    private sealed record QuestionStatusPayload(
        int SchemaVersion,
        CompetitionQuestionStatus From,
        CompetitionQuestionStatus To,
        CompetitionQuestionParticipantRole ActorRole);

    private sealed record QuestionAggregate(
        Notification RootNotification,
        QuestionRootPayload Root,
        List<Notification> Nodes)
    {
        public int Revision => Nodes.Count;

        public CompetitionQuestionStatus Status => Nodes.Count == 0
            ? Root.Status
            : Nodes[^1].Kind == NotificationKind.Message
                ? JsonSerializer.Deserialize<QuestionMessagePayload>(Nodes[^1].ContentJson, JsonOptions)!.To
                : JsonSerializer.Deserialize<QuestionStatusPayload>(Nodes[^1].ContentJson, JsonOptions)!.To;
    }
}
