using System.Data;
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
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder events,
    ILogger<CompetitionQuestionStore> logger,
    CompetitionQuestionAccessResolver? configuredAccessResolver = null)
    : ICompetitionQuestionStore
{
    private readonly CompetitionQuestionAccessResolver accessResolver =
        configuredAccessResolver ?? new(db);
    public async Task<CompetitionQuestionMutationResult> CreateAsync(
        CreateCompetitionQuestionCommand command,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await CreateOnceAsync(command, ct);
            }
            catch (Exception exception) when (attempt < 2
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }

    private async Task<CompetitionQuestionMutationResult> CreateOnceAsync(
        CreateCompetitionQuestionCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        var user = await db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == command.ActorUserId)
            .Select(candidate => new { candidate.AccountStatus })
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
                && candidate.Members.Any(member => member.UserId == command.ActorUserId)
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
            .ToArrayAsync(ct);
        if (recent.Any(root => root.Title == command.Title && root.Body == command.Body))
            return Failure(
                CompetitionQuestionFailure.SpamRejected,
                command.CompetitionId,
                null,
                team?.Id,
                command.ActorUserId);

        var root = new QuestionRootPayload(
            command.Subject,
            command.Title,
            command.Body,
            team!.Id,
            command.CompetitionChallengeId,
            command.GameplayFactId,
            CompetitionQuestionStatus.Pending);
        var notification = new QuestionOpenedNotification
        {
            Id = Guid.CreateVersion7(command.Now),
            SourceType = NotificationSourceType.User,
            SourceId = command.ActorUserId,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = command.CompetitionId,
            QuestionSubject = root.Subject,
            Title = root.Title,
            Body = root.Body,
            TeamId = root.TeamId,
            CompetitionChallengeId = root.CompetitionChallengeId,
            GameplayFactId = root.GameplayFactId,
            QuestionStatus = root.Status,
            QuestionActorRole = root.ActorRole,
            SentAt = command.Now,
            RelatedType = EntityReferenceKind.Competition,
            RelatedId = command.CompetitionId,
            ThreadRootId = null,
            ReplyToId = null
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
            command.Now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return new(await BuildViewAsync(
            new QuestionAggregate(notification, root, []),
            CompetitionQuestionAccess.Asker,
            true,
            ct));
    }

    public async Task<CompetitionQuestionMutationResult> CreateWriteUpConsultationAsync(
        CreateTeamWriteUpConsultationCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        var actor = await accessResolver.ResolveActorAsync(
            command.CompetitionId,
            command.ActorUserId,
            ct);
        var actorRole = actor switch
        {
            { IsPlatformAdministrator: true } =>
                CompetitionQuestionParticipantRole.PlatformAdministrator,
            { IsCompetitionManager: true } =>
                CompetitionQuestionParticipantRole.CompetitionManager,
            { IsJudge: true } => CompetitionQuestionParticipantRole.Judge,
            _ => (CompetitionQuestionParticipantRole?)null
        };
        if (actorRole is null)
        {
            return Failure(
                CompetitionQuestionFailure.Forbidden,
                command.CompetitionId,
                null,
                command.TeamId,
                command.ActorUserId);
        }

        var teamExists = await db.Teams.AsNoTracking().AnyAsync(team =>
            team.Id == command.TeamId
            && team.CompetitionId == command.CompetitionId
            && team.WriteUpFileId != null,
            ct);
        if (!teamExists)
        {
            return Failure(
                CompetitionQuestionFailure.SubmissionNotFound,
                command.CompetitionId,
                null,
                command.TeamId,
                command.ActorUserId);
        }
        if (command.CompetitionChallengeId is { } challengeId
            && !await db.CompetitionChallenges.AsNoTracking().AnyAsync(challenge =>
                challenge.Id == challengeId
                && challenge.CompetitionId == command.CompetitionId,
                ct))
        {
            return Failure(
                CompetitionQuestionFailure.InvalidChallengeReference,
                command.CompetitionId,
                null,
                command.TeamId,
                command.ActorUserId);
        }

        var duplicateCutoff = command.Now.AddMinutes(-1);
        var recent = await db.Notifications.AsNoTracking()
            .Where(notification => notification.Kind == NotificationKind.QuestionOpened
                && notification.SourceType == NotificationSourceType.User
                && notification.SourceId == command.ActorUserId
                && notification.RelatedType == EntityReferenceKind.Competition
                && notification.RelatedId == command.CompetitionId
                && notification.SentAt >= duplicateCutoff)
            .ToArrayAsync(ct);
        if (recent.Any(root =>
                root.TeamId == command.TeamId
                && root.Title == command.Title
                && root.Body == command.Body))
        {
            return Failure(
                CompetitionQuestionFailure.SpamRejected,
                command.CompetitionId,
                null,
                command.TeamId,
                command.ActorUserId);
        }

        var root = new QuestionRootPayload(
            command.CompetitionChallengeId is null
                ? CompetitionQuestionSubject.Platform
                : CompetitionQuestionSubject.Challenge,
            command.Title,
            command.Body,
            command.TeamId,
            command.CompetitionChallengeId,
            null,
            CompetitionQuestionStatus.Replied,
            actorRole.Value);
        var notification = new QuestionOpenedNotification
        {
            Id = Guid.CreateVersion7(command.Now),
            SourceType = NotificationSourceType.User,
            SourceId = command.ActorUserId,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = command.CompetitionId,
            QuestionSubject = root.Subject,
            Title = root.Title,
            Body = root.Body,
            TeamId = root.TeamId,
            CompetitionChallengeId = root.CompetitionChallengeId,
            GameplayFactId = root.GameplayFactId,
            QuestionStatus = root.Status,
            QuestionActorRole = root.ActorRole,
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
            TeamId: command.TeamId,
            CompetitionChallengeId: command.CompetitionChallengeId,
            QuestionId: notification.Id,
            QuestionStatus: CompetitionQuestionStatus.Replied), ct);
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            command.CompetitionId,
            notification.Id,
            null,
            await ResolveTeamRecipientIdsAsync(command.TeamId, ct),
            NotificationKind.QuestionOpened,
            CompetitionQuestionNotificationEvent.Opened,
            root.Title,
            command.Now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return new(await BuildViewAsync(
            new QuestionAggregate(notification, root, []),
            CompetitionQuestionAccess.Handler,
            true,
            ct));
    }

    public async Task<CompetitionQuestionPage> ListAsync(
        CompetitionQuestionQuery query,
        CancellationToken ct)
    {
        var actor = await accessResolver.ResolveActorAsync(query.CompetitionId, query.ActorUserId, ct);
        if (actor is null)
            return new([], null);
        var limit = Math.Clamp(query.Limit, 1, CompetitionQuestionRules.MaximumListLimit);
        var ownedChallengeIds = await accessResolver.ResolveOwnedChallengeIdsAsync(
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
                Resolution = CompetitionQuestionAccessResolver.Resolve(aggregate, actor, ownedChallengeIds)
            })
            .Where(input => input.Resolution is not null)
            .Select(input => new QuestionViewInput(
                input.Question,
                input.Resolution!.Value.Access))
            .ToArray();
        var hasMore = visible.Length > limit;
        var page = visible.Take(limit).ToArray();
        var items = await BuildViewsAsync(page, false, ct);
        return new(
            items,
            hasMore && items.Length > 0
                ? new(items[^1].UpdatedAt, items[^1].ThreadRootId)
                : null);
    }

    public async Task<CompetitionQuestionView?> FindAsync(
        Guid competitionId,
        Guid questionId,
        Guid actorUserId,
        CancellationToken ct)
    {
        var aggregate = await LoadAggregateAsync(competitionId, questionId, ct);
        var actor = await accessResolver.ResolveActorAsync(competitionId, actorUserId, ct);
        if (aggregate is null || actor is null)
            return null;
        var access = await accessResolver.ResolveAsync(aggregate, actorUserId, actor, ct);
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
        var actor = await accessResolver.ResolveActorAsync(command.CompetitionId, command.ActorUserId, ct);
        if (aggregate is null)
            return Failure(
                CompetitionQuestionFailure.NotFound,
                command.CompetitionId,
                command.QuestionId,
                null,
                command.ActorUserId);
        var access = actor is null
            ? null
            : await accessResolver.ResolveAsync(aggregate, command.ActorUserId, actor, ct);
        if (access is null
            || access.Value.Access is not (CompetitionQuestionAccess.Asker
                or CompetitionQuestionAccess.Handler))
            return Failure(
                CompetitionQuestionFailure.Forbidden,
                command.CompetitionId,
                command.QuestionId,
                aggregate.Root.TeamId,
                command.ActorUserId);
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
            var participantMessages = CompetitionQuestionViewProjection
                .CountParticipantMessagesSinceHandlerReply(aggregate);
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
            command.Body,
            aggregate.Status,
            nextStatus.Value,
            actorRole);
        var node = AppendNode(
            aggregate,
            command.ActorUserId,
            NotificationKind.Message,
            payload,
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
            command.Now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
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
        var actor = await accessResolver.ResolveActorAsync(command.CompetitionId, command.ActorUserId, ct);
        if (aggregate is null)
            return Failure(
                CompetitionQuestionFailure.NotFound,
                command.CompetitionId,
                command.QuestionId,
                null,
                command.ActorUserId);
        var access = actor is null
            ? null
            : await accessResolver.ResolveAsync(aggregate, command.ActorUserId, actor, ct);
        if (access is null
            || access.Value.Access is not (CompetitionQuestionAccess.Asker
                or CompetitionQuestionAccess.Handler))
            return Failure(
                CompetitionQuestionFailure.Forbidden,
                command.CompetitionId,
                command.QuestionId,
                aggregate.Root.TeamId,
                command.ActorUserId);
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
        var payload = new QuestionStatusPayload(aggregate.Status, command.Status, role);
        var node = AppendNode(
            aggregate,
            command.ActorUserId,
            NotificationKind.QuestionStatusChanged,
            payload,
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
            command.Now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        aggregate.Nodes.Add(node);
        return new(await BuildViewAsync(aggregate, access.Value.Access, true, ct));
    }

    private async Task AcquireQuestionLockAsync(Guid questionId, CancellationToken ct)
    {
        _ = await db.Notifications.AsNoTracking()
            .AnyAsync(item => item.Id == questionId, ct);
    }

    private async Task AcquireTeamQuestionLockAsync(Guid teamId, CancellationToken ct)
    {
        _ = await db.Teams.AsNoTracking().AnyAsync(item => item.Id == teamId, ct);
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
                ManagerIds = candidate.Collaborators
                    .Where(item => item.Role == CompetitionCollaboratorRole.Manager)
                    .Select(item => item.UserId).ToArray(),
                JudgeIds = candidate.Collaborators
                    .Where(item => item.Role == CompetitionCollaboratorRole.Judge)
                    .Select(item => item.UserId).ToArray(),
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
                        ManagerIds = challenge.Managers.Select(item => item.UserId).ToArray()
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
                && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        candidateIds.AddRange(administratorIds);
        var distinctIds = candidateIds.Distinct().ToArray();
        return await db.Users.AsNoTracking()
            .Where(user => distinctIds.Contains(user.Id)
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
            .Select(team => team.Members.Select(member => member.UserId).ToArray())
            .SingleOrDefaultAsync(ct) ?? [];
        return await db.Users.AsNoTracking()
            .Where(user => memberIds.Contains(user.Id)
                && user.AccountStatus == UserAccountStatus.Active)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
    }

    private static Notification AppendNode(
        QuestionAggregate aggregate,
        Guid actorUserId,
        NotificationKind kind,
        object payload,
        DateTimeOffset sentAt)
    {
        var notification = NotificationGeneratedCatalog.Create(kind);
        notification.Id = Guid.CreateVersion7(sentAt);
        notification.SourceType = NotificationSourceType.User;
        notification.SourceId = actorUserId;
        notification.TargetType = aggregate.RootNotification.TargetType;
        notification.TargetId = aggregate.RootNotification.TargetId;
        switch (payload)
        {
            case QuestionMessagePayload message:
                notification.Body = message.Body;
                notification.PreviousQuestionStatus = message.From;
                notification.QuestionStatus = message.To;
                notification.QuestionActorRole = message.ActorRole;
                break;
            case QuestionStatusPayload status:
                notification.PreviousQuestionStatus = status.From;
                notification.QuestionStatus = status.To;
                notification.QuestionActorRole = status.ActorRole;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(payload),
                    payload.GetType(),
                    "Unsupported question notification payload.");
        }
        notification.SentAt = sentAt;
        notification.RelatedType = aggregate.RootNotification.RelatedType;
        notification.RelatedId = aggregate.RootNotification.RelatedId;
        notification.ThreadRootId = aggregate.RootNotification.Id;
        notification.ReplyToId = aggregate.RootNotification.Id;
        return notification;
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
        var payload = root is null ? null : ParseRoot(root);
        return root is null || payload is null ? null : await LoadAggregateAsync(root, payload, ct);
    }

    private async Task<Notification[]> LoadQuestionRootsAsync(
        CompetitionQuestionQuery query,
        ActorContext actor,
        IReadOnlyCollection<Guid> ownedChallengeIds,
        CancellationToken ct)
    {
        var limit = checked(Math.Clamp(
            query.Limit,
            1,
            CompetitionQuestionRules.MaximumListLimit) + 1);
        var candidates = await db.Notifications.AsNoTracking()
            .Where(notification => notification.Kind == NotificationKind.QuestionOpened
                && notification.TargetType == NotificationTargetType.CompetitionCollaborators
                && notification.TargetId == query.CompetitionId)
            .ToArrayAsync(ct);
        var aggregates = await LoadAggregatesAsync(candidates, ct);
        return aggregates
            .Where(aggregate => MatchesQuery(aggregate, query)
                && CompetitionQuestionAccessResolver.Resolve(
                    aggregate,
                    actor,
                    ownedChallengeIds) is not null
                && IsBeforePosition(aggregate, query.Position))
            .OrderByDescending(aggregate => aggregate.UpdatedAt)
            .ThenByDescending(aggregate => aggregate.RootNotification.Id)
            .Take(limit)
            .Select(aggregate => aggregate.RootNotification)
            .ToArray();
    }

    private async Task<QuestionAggregate[]> LoadAggregatesAsync(
        IReadOnlyCollection<Notification> roots,
        CancellationToken ct)
    {
        if (roots.Count == 0)
            return [];
        var rootPayloads = roots
            .Select(root => new { Notification = root, Payload = ParseRoot(root) })
            .Where(item => item.Payload is not null)
            .ToDictionary(item => item.Notification.Id, item => item);
        if (rootPayloads.Count == 0)
            return [];

        var rootIds = rootPayloads.Keys.ToArray();
        var descendants = await db.Notifications.AsNoTracking()
            .Where(notification => notification.ThreadRootId != null
                && rootIds.Contains(notification.ThreadRootId.Value)
                && notification.TargetType == NotificationTargetType.CompetitionCollaborators)
            .ToArrayAsync(ct);

        var nodesByRoot = rootPayloads.Keys.ToDictionary(id => id, _ => new List<Notification>());
        foreach (var descendant in descendants)
        {
            if (descendant.ThreadRootId is { } rootId
                && nodesByRoot.TryGetValue(rootId, out var nodes))
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
        var nodes = await db.Notifications.AsNoTracking()
            .Where(notification => notification.ThreadRootId == root.Id
                && notification.TargetType == NotificationTargetType.CompetitionCollaborators)
            .OrderBy(notification => notification.SentAt)
            .ThenBy(notification => notification.Id)
            .ToListAsync(ct);
        return new(root, payload, nodes);
    }

    private async Task<int> CountActiveQuestionsAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct)
    {
        var roots = await db.Notifications.AsNoTracking()
            .Where(notification =>
                notification.Kind == NotificationKind.QuestionOpened
                && notification.TargetType == NotificationTargetType.CompetitionCollaborators
                && notification.TargetId == competitionId
                && notification.TeamId == teamId)
            .ToArrayAsync(ct);
        return (await LoadAggregatesAsync(roots, ct)).Count(aggregate =>
            aggregate.Status is CompetitionQuestionStatus.Pending
                or CompetitionQuestionStatus.Replied);
    }

    private static bool MatchesQuery(
        QuestionAggregate aggregate,
        CompetitionQuestionQuery query) =>
        (query.CompetitionChallengeId is null
            || aggregate.Root.CompetitionChallengeId == query.CompetitionChallengeId)
        && (query.Subject is null || aggregate.Root.Subject == query.Subject)
        && (query.Status is null || aggregate.Status == query.Status);

    private static bool IsBeforePosition(
        QuestionAggregate aggregate,
        CompetitionQuestionPagePosition? position) =>
        position is null
        || aggregate.UpdatedAt < position.UpdatedAt
        || aggregate.UpdatedAt == position.UpdatedAt
        && aggregate.RootNotification.Id.CompareTo(position.Id) < 0;

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
                (binding, challenge) => new
                {
                    binding.Id,
                    Title = binding.CustomTitle ?? challenge.Title
                })
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
        return questions.Select(input => CompetitionQuestionViewProjection.Build(
            input.Question,
            input.Access,
            includeEntries,
            names,
            teamNames,
            challengeTitles,
            participantMessageLimits[input.Question.RootNotification.TargetId])).ToArray();
    }

    private static QuestionRootPayload? ParseRoot(Notification notification) =>
        notification is QuestionOpenedNotification
        && notification.QuestionSubject is { } subject
        && notification.Title is { } title
        && notification.Body is { } body
        && notification.TeamId is { } teamId
        && notification.QuestionStatus is { } status
            ? new QuestionRootPayload(
                subject,
                title,
                body,
                teamId,
                notification.CompetitionChallengeId,
                notification.GameplayFactId,
                status,
                notification.QuestionActorRole)
            : null;

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

    internal sealed record ActorContext(
        bool IsPlatformAdministrator,
        bool IsCompetitionManager,
        bool IsJudge,
        bool IsObserver,
        Guid? TeamId,
        int MaxActiveQuestionsPerTeam,
        int MaxParticipantMessagesBeforeHandlerReply,
        bool AllowChallengeOwnersToHandleQuestions);

    internal readonly record struct AccessResolution(
        CompetitionQuestionAccess Access,
        CompetitionQuestionParticipantRole ActorRole);

    internal readonly record struct QuestionViewInput(
        QuestionAggregate Question,
        CompetitionQuestionAccess Access);

    internal sealed record QuestionRootPayload(
        CompetitionQuestionSubject Subject,
        string Title,
        string Body,
        Guid TeamId,
        Guid? CompetitionChallengeId,
        Guid? GameplayFactId,
        CompetitionQuestionStatus Status,
        CompetitionQuestionParticipantRole? ActorRole = null);

    internal sealed record QuestionMessagePayload(
        string Body,
        CompetitionQuestionStatus From,
        CompetitionQuestionStatus To,
        CompetitionQuestionParticipantRole ActorRole);

    internal sealed record QuestionStatusPayload(
        CompetitionQuestionStatus From,
        CompetitionQuestionStatus To,
        CompetitionQuestionParticipantRole ActorRole);

    internal sealed record QuestionAggregate(
        Notification RootNotification,
        QuestionRootPayload Root,
        List<Notification> Nodes)
    {
        public DateTimeOffset UpdatedAt => Nodes.Count == 0
            ? RootNotification.SentAt
            : Nodes[^1].SentAt;

        public CompetitionQuestionStatus Status
        {
            get
            {
                var statusNode = Nodes.LastOrDefault(node => node.Kind is
                    NotificationKind.Message or NotificationKind.QuestionStatusChanged);
                if (statusNode is null)
                    return Root.Status;
                return statusNode.QuestionStatus
                    ?? throw new InvalidOperationException(
                        $"Question node {statusNode.Id} has no resulting status.");
            }
        }
    }
}
