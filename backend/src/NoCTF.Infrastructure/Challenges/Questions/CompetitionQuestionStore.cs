using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Questions;

public sealed class CompetitionQuestionStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox) : ICompetitionQuestionStore
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
            .Select(candidate => new { candidate.Status })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return Failure(CompetitionQuestionFailure.NotFound);

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
        var hasValidChallenge = command.Subject switch
        {
            CompetitionQuestionSubject.Challenge =>
                command.CompetitionChallengeId is not null
                && await db.CompetitionChallenges.AsNoTracking().AnyAsync(candidate =>
                    candidate.Id == command.CompetitionChallengeId
                    && candidate.CompetitionId == command.CompetitionId
                    && candidate.IsPublished, ct),
            CompetitionQuestionSubject.Platform => command.CompetitionChallengeId is null,
            _ => false
        };
        var hasValidSubmission = command.SubmissionId is null
            || team is not null && await db.Submissions.AsNoTracking().AnyAsync(submission =>
                submission.Id == command.SubmissionId
                && submission.CompetitionId == command.CompetitionId
                && submission.TeamId == team.Id
                && (command.Subject == CompetitionQuestionSubject.Platform
                    || submission.CompetitionChallengeId == command.CompetitionChallengeId), ct);
        var context = new CompetitionQuestionCreationContext(
            user is { Kind: UserKind.Human, AccountStatus: UserAccountStatus.Active },
            competition.Status,
            hasApprovedTeam,
            command.Subject,
            hasValidChallenge,
            hasValidSubmission);
        if (CompetitionQuestionRules.ValidateCreation(context) is { } contextFailure)
            return Failure(contextFailure);

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
            return Failure(CompetitionQuestionFailure.SpamRejected);

        var root = new QuestionRootPayload(
            1,
            command.Subject,
            command.Title,
            command.Body,
            team!.Id,
            command.CompetitionChallengeId,
            command.SubmissionId,
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
        var roots = await db.Notifications.AsNoTracking()
            .Where(notification => notification.Kind == NotificationKind.QuestionOpened
                && notification.TargetType == NotificationTargetType.CompetitionCollaborators
                && notification.TargetId == query.CompetitionId)
            .OrderByDescending(notification => notification.SentAt)
            .ThenByDescending(notification => notification.Id)
            .ToArrayAsync(ct);
        var views = new List<CompetitionQuestionView>();
        foreach (var rootNotification in roots)
        {
            var root = ParseRoot(rootNotification.ContentJson);
            if (root is null
                || query.CompetitionChallengeId is not null
                    && root.CompetitionChallengeId != query.CompetitionChallengeId
                || query.Subject is not null && root.Subject != query.Subject)
                continue;
            var aggregate = await LoadAggregateAsync(rootNotification, root, ct);
            if (query.Status is not null && aggregate.Status != query.Status)
                continue;
            var access = await ResolveAccessAsync(aggregate, query.ActorUserId, actor, ct);
            if (access is null)
                continue;
            views.Add(await BuildViewAsync(aggregate, access.Value, false, ct));
            if (views.Count == query.Limit)
                break;
        }
        return views;
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
            : await BuildViewAsync(aggregate, access.Value, true, ct);
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
            return Failure(CompetitionQuestionFailure.NotFound);
        var access = actor is null
            ? null
            : await ResolveAccessAsync(aggregate, command.ActorUserId, actor, ct);
        if (access is not (CompetitionQuestionAccess.Asker or CompetitionQuestionAccess.Handler))
            return Failure(CompetitionQuestionFailure.Forbidden);
        if (aggregate.Revision != command.ExpectedRevision)
            return new(await BuildViewAsync(aggregate, access.Value, true, ct),
                CompetitionQuestionFailure.RevisionConflict);

        var actorRole = access == CompetitionQuestionAccess.Asker
            ? CompetitionQuestionParticipantRole.Asker
            : CompetitionQuestionParticipantRole.Handler;
        var nextStatus = CompetitionQuestionRules.StatusAfterMessage(aggregate.Status, actorRole);
        if (nextStatus is null)
            return Failure(aggregate.Status == CompetitionQuestionStatus.Closed
                ? CompetitionQuestionFailure.QuestionClosed
                : CompetitionQuestionFailure.InvalidTransition);
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
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            command.CompetitionId,
            aggregate.RootNotification.Id,
            node.Id,
            actorRole == CompetitionQuestionParticipantRole.Asker
                ? await ResolveHandlerRecipientIdsAsync(
                    command.CompetitionId,
                    aggregate.Root.CompetitionChallengeId,
                    ct)
                : [aggregate.RootNotification.SourceId!.Value],
            NotificationKind.Message,
            actorRole == CompetitionQuestionParticipantRole.Asker
                ? CompetitionQuestionNotificationEvent.AskerFollowedUp
                : CompetitionQuestionNotificationEvent.HandlerReplied,
            aggregate.Root.Title,
            command.Now,
            aggregate.Revision + 1));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        aggregate.Nodes.Add(node);
        return new(await BuildViewAsync(aggregate, access.Value, true, ct));
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
            return Failure(CompetitionQuestionFailure.NotFound);
        var access = actor is null
            ? null
            : await ResolveAccessAsync(aggregate, command.ActorUserId, actor, ct);
        if (access is not (CompetitionQuestionAccess.Asker or CompetitionQuestionAccess.Handler))
            return Failure(CompetitionQuestionFailure.Forbidden);
        if (aggregate.Revision != command.ExpectedRevision)
            return new(await BuildViewAsync(aggregate, access.Value, true, ct),
                CompetitionQuestionFailure.RevisionConflict);
        var role = access == CompetitionQuestionAccess.Asker
            ? CompetitionQuestionParticipantRole.Asker
            : CompetitionQuestionParticipantRole.Handler;
        if (!CompetitionQuestionRules.CanTransition(aggregate.Status, command.Status, role))
            return Failure(aggregate.Status == CompetitionQuestionStatus.Closed
                ? CompetitionQuestionFailure.QuestionClosed
                : CompetitionQuestionFailure.InvalidTransition);
        var payload = new QuestionStatusPayload(1, aggregate.Status, command.Status, role);
        var node = AppendNode(
            aggregate,
            command.ActorUserId,
            NotificationKind.QuestionStatusChanged,
            JsonSerializer.Serialize(payload, JsonOptions),
            command.Now);
        db.Notifications.Add(node);
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            command.CompetitionId,
            aggregate.RootNotification.Id,
            node.Id,
            role == CompetitionQuestionParticipantRole.Asker
                ? await ResolveHandlerRecipientIdsAsync(
                    command.CompetitionId,
                    aggregate.Root.CompetitionChallengeId,
                    ct)
                : [aggregate.RootNotification.SourceId!.Value],
            NotificationKind.QuestionStatusChanged,
            CompetitionQuestionNotificationEvent.StatusChanged,
            aggregate.Root.Title,
            command.Now,
            aggregate.Revision + 1));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        aggregate.Nodes.Add(node);
        return new(await BuildViewAsync(aggregate, access.Value, true, ct));
    }

    private async Task AcquireQuestionLockAsync(Guid questionId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({questionId.ToString()}, 0))",
            ct);

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
                candidate.JudgeIds
            })
            .SingleAsync(ct);
        var candidateIds = competition.ManagerIds
            .Concat(competition.JudgeIds)
            .Append(competition.OwnerId)
            .ToList();
        if (competitionChallengeId is Guid bindingId)
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

    private async Task<QuestionAggregate> LoadAggregateAsync(
        Notification root,
        QuestionRootPayload payload,
        CancellationToken ct)
    {
        var nodes = await db.Notifications.FromSqlInterpolated($$"""
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
        return new(root, payload, nodes);
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
                candidate.ObserverIds
            })
            .SingleOrDefaultAsync(ct);
        if (user is null || competition is null)
            return null;
        var handler = user.Role == UserRole.Administrator
            || competition.OwnerId == actorUserId
            || competition.ManagerIds.Contains(actorUserId)
            || competition.JudgeIds.Contains(actorUserId);
        return new(
            handler,
            !handler && competition.ObserverIds.Contains(actorUserId),
            await db.Teams.AsNoTracking().AnyAsync(team =>
                team.CompetitionId == competitionId
                && team.MemberIds.Contains(actorUserId)
                && team.DeletedAt == null
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned, ct));
    }

    private async Task<CompetitionQuestionAccess?> ResolveAccessAsync(
        QuestionAggregate question,
        Guid actorUserId,
        ActorContext actor,
        CancellationToken ct)
    {
        if (actor.IsCompetitionHandler)
            return CompetitionQuestionAccess.Handler;
        if (actor.IsObserver)
            return CompetitionQuestionAccess.Observer;
        if (question.RootNotification.SourceId == actorUserId)
            return CompetitionQuestionAccess.Asker;
        if (question.Root.CompetitionChallengeId is { } challengeId
            && await db.CompetitionChallenges.AsNoTracking().AnyAsync(binding =>
                binding.Id == challengeId
                && db.Challenges.Any(challenge => challenge.Id == binding.ChallengeId
                    && (challenge.OwnerId == actorUserId
                        || challenge.ManagerIds.Contains(actorUserId))), ct))
            return CompetitionQuestionAccess.Handler;
        return null;
    }

    private async Task<CompetitionQuestionView> BuildViewAsync(
        QuestionAggregate question,
        CompetitionQuestionAccess access,
        bool includeEntries,
        CancellationToken ct)
    {
        var askerId = question.RootNotification.SourceId!.Value;
        var actorIds = question.Nodes.Select(node => node.SourceId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Append(askerId)
            .Distinct()
            .ToArray();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        var teamName = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(team => team.Id == question.Root.TeamId)
            .Select(team => team.Name)
            .SingleOrDefaultAsync(ct);
        var entries = includeEntries
            ? question.Nodes.Select(node => MapEntry(node, names))
                .Prepend(new CompetitionQuestionEntryView(
                    question.RootNotification.Id,
                    CompetitionQuestionEntryKind.StatusTransition,
                    CompetitionQuestionParticipantRole.Asker,
                    askerId,
                    names.GetValueOrDefault(askerId, "已删除用户"),
                    null,
                    null,
                    question.Root.Status,
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
            teamName,
            question.Root.SubmissionId,
            question.Root.Subject,
            question.Root.Title,
            question.Root.Body,
            question.Status,
            access,
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

    private static CompetitionQuestionMutationResult Failure(CompetitionQuestionFailure failure) =>
        new(null, failure);

    private sealed record ActorContext(
        bool IsCompetitionHandler,
        bool IsObserver,
        bool IsApprovedParticipant);

    private sealed record QuestionRootPayload(
        int SchemaVersion,
        CompetitionQuestionSubject Subject,
        string Title,
        string Body,
        Guid TeamId,
        Guid? CompetitionChallengeId,
        Guid? SubmissionId,
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
