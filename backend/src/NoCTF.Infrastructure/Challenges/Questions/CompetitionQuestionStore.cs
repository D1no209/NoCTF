using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Questions;

public sealed class CompetitionQuestionStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox) : ICompetitionQuestionStore
{
    public async Task<CompetitionQuestionMutationResult> CreateAsync(
        CreateCompetitionQuestionCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
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
        var challengeId = command.CompetitionChallengeId is null
            ? null
            : await db.CompetitionChallenges.AsNoTracking()
                .Where(candidate => candidate.Id == command.CompetitionChallengeId
                    && candidate.CompetitionId == command.CompetitionId
                    && candidate.IsPublished)
                .Select(candidate => (Guid?)candidate.ChallengeId)
                .SingleOrDefaultAsync(ct);
        var hasValidChallenge = command.Subject switch
        {
            CompetitionQuestionSubject.Challenge => challengeId is not null,
            CompetitionQuestionSubject.Platform => command.CompetitionChallengeId is not null,
            _ => false
        };
        var hasValidSubmission = command.SubmissionId is null
            || team is not null && await db.Submissions.AsNoTracking().AnyAsync(submission =>
                submission.Id == command.SubmissionId
                && submission.CompetitionId == command.CompetitionId
                && submission.TeamId == team.Id
                && (command.Subject == CompetitionQuestionSubject.Platform
                    || submission.CompetitionChallengeId == command.CompetitionChallengeId),
                ct);
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
        if (await db.CompetitionQuestions.AsNoTracking().AnyAsync(question =>
            question.CompetitionId == command.CompetitionId
            && question.TeamId == team!.Id
            && question.AskedByUserId == command.ActorUserId
            && question.Title == command.Title
            && question.Body == command.Body
            && question.CreatedAt >= duplicateCutoff,
            ct))
            return Failure(CompetitionQuestionFailure.SpamRejected);

        var question = new CompetitionQuestion
        {
            Id = Guid.CreateVersion7(command.Now),
            CompetitionId = command.CompetitionId,
            CompetitionChallengeId = command.Subject == CompetitionQuestionSubject.Challenge
                ? command.CompetitionChallengeId
                : null,
            TeamId = team!.Id,
            AskedByUserId = command.ActorUserId,
            SubmissionId = command.SubmissionId,
            Subject = command.Subject,
            Title = command.Title,
            Body = command.Body,
            Status = CompetitionQuestionStatus.Pending,
            Revision = 0,
            CreatedAt = command.Now,
            UpdatedAt = command.Now
        };
        db.CompetitionQuestions.Add(question);
        var recipients = await ResolveHandlerRecipientIdsAsync(
            command.CompetitionId,
            challengeId,
            command.ActorUserId,
            ct);
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            command.CompetitionId,
            question.Id,
            null,
            recipients,
            NotificationKind.CompetitionQuestionOpened,
            CompetitionQuestionNotificationEvent.Opened,
            question.Title,
            command.Now,
            question.Revision));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(await BuildViewAsync(question, CompetitionQuestionAccess.Asker, true, ct));
    }

    public async Task<IReadOnlyList<CompetitionQuestionView>> ListAsync(
        CompetitionQuestionQuery query,
        CancellationToken ct)
    {
        var actor = await ResolveActorContextAsync(query.CompetitionId, query.ActorUserId, ct);
        if (actor is null)
            return [];
        var authoredChallengeIds = actor.IsCompetitionHandler || actor.IsObserver
            ? []
            : await db.Challenges.AsNoTracking()
                .Where(challenge => challenge.OwnerId == query.ActorUserId
                    || challenge.ManagerIds.Contains(query.ActorUserId))
                .Select(challenge => challenge.Id)
                .ToArrayAsync(ct);
        var questions = db.CompetitionQuestions.AsNoTracking()
            .Where(question => question.CompetitionId == query.CompetitionId);
        if (query.CompetitionChallengeId is not null)
            questions = questions.Where(question =>
                question.CompetitionChallengeId == query.CompetitionChallengeId);
        if (query.Subject is not null)
            questions = questions.Where(question => question.Subject == query.Subject);
        if (query.Status is not null)
            questions = questions.Where(question => question.Status == query.Status);
        if (query.PublishedOnly)
            questions = questions.Where(question => question.PublishedAt != null);
        if (!actor.IsCompetitionHandler && !actor.IsObserver)
        {
            questions = questions.Where(question =>
                question.AskedByUserId == query.ActorUserId
                || actor.IsApprovedParticipant && question.PublishedAt != null
                || question.CompetitionChallengeId != null
                && db.CompetitionChallenges.Any(binding =>
                    binding.Id == question.CompetitionChallengeId
                    && authoredChallengeIds.Contains(binding.ChallengeId)));
        }
        var entities = await questions
            .OrderByDescending(question => question.UpdatedAt)
            .ThenByDescending(question => question.Id)
            .Take(query.Limit)
            .ToArrayAsync(ct);
        var views = new List<CompetitionQuestionView>(entities.Length);
        foreach (var question in entities)
        {
            var access = await ResolveAccessAsync(question, query.ActorUserId, actor, ct);
            if (access is not null)
                views.Add(await BuildViewAsync(question, access.Value, false, ct));
        }
        return views;
    }

    public async Task<CompetitionQuestionView?> FindAsync(
        Guid competitionId,
        Guid questionId,
        Guid actorUserId,
        CancellationToken ct)
    {
        var actor = await ResolveActorContextAsync(competitionId, actorUserId, ct);
        if (actor is null)
            return null;
        var question = await db.CompetitionQuestions.AsNoTracking()
            .Include(candidate => candidate.Entries)
            .SingleOrDefaultAsync(candidate => candidate.Id == questionId
                && candidate.CompetitionId == competitionId, ct);
        if (question is null)
            return null;
        var access = await ResolveAccessAsync(question, actorUserId, actor, ct);
        return access is null
            ? null
            : await BuildViewAsync(question, access.Value, true, ct);
    }

    public async Task<CompetitionQuestionMutationResult> AddMessageAsync(
        AddCompetitionQuestionMessageCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        var question = await db.CompetitionQuestions
            .Include(candidate => candidate.Entries)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.QuestionId
                && candidate.CompetitionId == command.CompetitionId, ct);
        if (question is null)
            return Failure(CompetitionQuestionFailure.NotFound);
        var actor = await ResolveActorContextAsync(command.CompetitionId, command.ActorUserId, ct);
        var access = actor is null
            ? null
            : await ResolveAccessAsync(question, command.ActorUserId, actor, ct);
        if (access is not (CompetitionQuestionAccess.Asker or CompetitionQuestionAccess.Handler))
            return Failure(CompetitionQuestionFailure.Forbidden);
        if (question.Revision != command.ExpectedRevision)
            return new(
                await BuildViewAsync(question, access.Value, true, ct),
                CompetitionQuestionFailure.RevisionConflict);
        var actorRole = access == CompetitionQuestionAccess.Asker
            ? CompetitionQuestionParticipantRole.Asker
            : CompetitionQuestionParticipantRole.Handler;
        var nextStatus = CompetitionQuestionRules.StatusAfterMessage(question.Status, actorRole);
        if (nextStatus is null)
            return Failure(question.Status == CompetitionQuestionStatus.Closed
                ? CompetitionQuestionFailure.QuestionClosed
                : CompetitionQuestionFailure.InvalidTransition);

        var entry = new CompetitionQuestionEntry
        {
            Id = Guid.CreateVersion7(command.Now),
            QuestionId = question.Id,
            Kind = CompetitionQuestionEntryKind.Message,
            ActorRole = actorRole,
            ActorUserId = command.ActorUserId,
            Body = command.Body,
            CreatedAt = command.Now
        };
        question.Entries.Add(entry);
        db.Set<CompetitionQuestionEntry>().Add(entry);
        if (nextStatus != question.Status)
        {
            var transition = StatusTransition(
                question,
                command.ActorUserId,
                actorRole,
                nextStatus.Value,
                command.Now);
            question.Entries.Add(transition);
            db.Set<CompetitionQuestionEntry>().Add(transition);
            question.Status = nextStatus.Value;
        }
        question.Revision = checked(question.Revision + 1);
        question.UpdatedAt = command.Now;
        var recipients = actorRole == CompetitionQuestionParticipantRole.Handler
            ? [question.AskedByUserId]
            : await ResolveHandlerRecipientIdsAsync(
                question.CompetitionId,
                await ResolveTemplateChallengeIdAsync(question.CompetitionChallengeId, ct),
                command.ActorUserId,
                ct);
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            question.CompetitionId,
            question.Id,
            entry.Id,
            recipients,
            NotificationKind.CompetitionQuestionReplied,
            actorRole == CompetitionQuestionParticipantRole.Handler
                ? CompetitionQuestionNotificationEvent.HandlerReplied
                : CompetitionQuestionNotificationEvent.AskerFollowedUp,
            question.Title,
            command.Now,
            question.Revision));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(await BuildViewAsync(question, access.Value, true, ct));
    }

    public async Task<CompetitionQuestionMutationResult> ChangeStatusAsync(
        ChangeCompetitionQuestionStatusCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        var question = await db.CompetitionQuestions
            .Include(candidate => candidate.Entries)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.QuestionId
                && candidate.CompetitionId == command.CompetitionId, ct);
        if (question is null)
            return Failure(CompetitionQuestionFailure.NotFound);
        var actor = await ResolveActorContextAsync(command.CompetitionId, command.ActorUserId, ct);
        var access = actor is null
            ? null
            : await ResolveAccessAsync(question, command.ActorUserId, actor, ct);
        if (access is not (CompetitionQuestionAccess.Asker or CompetitionQuestionAccess.Handler))
            return Failure(CompetitionQuestionFailure.Forbidden);
        if (question.Revision != command.ExpectedRevision)
            return new(
                await BuildViewAsync(question, access.Value, true, ct),
                CompetitionQuestionFailure.RevisionConflict);
        var actorRole = access == CompetitionQuestionAccess.Asker
            ? CompetitionQuestionParticipantRole.Asker
            : CompetitionQuestionParticipantRole.Handler;
        if (!CompetitionQuestionRules.CanTransition(question.Status, command.Status, actorRole))
            return Failure(question.Status == CompetitionQuestionStatus.Closed
                ? CompetitionQuestionFailure.QuestionClosed
                : CompetitionQuestionFailure.InvalidTransition);
        var entry = StatusTransition(
            question,
            command.ActorUserId,
            actorRole,
            command.Status,
            command.Now);
        question.Entries.Add(entry);
        db.Set<CompetitionQuestionEntry>().Add(entry);
        question.Status = command.Status;
        question.Revision = checked(question.Revision + 1);
        question.UpdatedAt = command.Now;
        var recipients = actorRole == CompetitionQuestionParticipantRole.Handler
            ? [question.AskedByUserId]
            : await ResolveHandlerRecipientIdsAsync(
                question.CompetitionId,
                await ResolveTemplateChallengeIdAsync(question.CompetitionChallengeId, ct),
                command.ActorUserId,
                ct);
        await outbox.PublishAsync(new DeliverCompetitionQuestionNotification(
            question.CompetitionId,
            question.Id,
            entry.Id,
            recipients,
            NotificationKind.CompetitionQuestionStatusChanged,
            CompetitionQuestionNotificationEvent.StatusChanged,
            question.Title,
            command.Now,
            question.Revision));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(await BuildViewAsync(question, access.Value, true, ct));
    }

    public async Task<CompetitionQuestionMutationResult> PublishAsync(
        PublishCompetitionQuestionCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        var question = await db.CompetitionQuestions
            .Include(candidate => candidate.Entries)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.QuestionId
                && candidate.CompetitionId == command.CompetitionId, ct);
        if (question is null)
            return Failure(CompetitionQuestionFailure.NotFound);
        var actor = await ResolveActorContextAsync(command.CompetitionId, command.ActorUserId, ct);
        var access = actor is null
            ? null
            : await ResolveAccessAsync(question, command.ActorUserId, actor, ct);
        if (access != CompetitionQuestionAccess.Handler)
            return Failure(CompetitionQuestionFailure.Forbidden);
        if (question.Revision != command.ExpectedRevision)
            return new(
                await BuildViewAsync(question, access.Value, true, ct),
                CompetitionQuestionFailure.RevisionConflict);
        var reply = question.Entries.SingleOrDefault(entry =>
            entry.Id == command.ReplyEntryId
            && entry.Kind == CompetitionQuestionEntryKind.Message
            && entry.ActorRole == CompetitionQuestionParticipantRole.Handler);
        if (reply is null)
            return Failure(CompetitionQuestionFailure.ReplyNotPublishable);
        if (reply.PublishedAt is not null && question.PublishedAt is not null)
            return new(await BuildViewAsync(question, access.Value, true, ct));

        question.PublishedAt ??= command.Now;
        question.PublishedByUserId ??= command.ActorUserId;
        reply.PublishedAt ??= command.Now;
        reply.PublishedByUserId ??= command.ActorUserId;
        var publication = new CompetitionQuestionEntry
        {
            Id = Guid.CreateVersion7(command.Now),
            QuestionId = question.Id,
            Kind = CompetitionQuestionEntryKind.Publication,
            ActorRole = CompetitionQuestionParticipantRole.Handler,
            ActorUserId = command.ActorUserId,
            TargetEntryId = reply.Id,
            CreatedAt = command.Now
        };
        question.Entries.Add(publication);
        db.Set<CompetitionQuestionEntry>().Add(publication);
        question.Revision = checked(question.Revision + 1);
        question.UpdatedAt = command.Now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(await BuildViewAsync(question, access.Value, true, ct));
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
        if (user is null)
            return null;
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
        if (competition is null)
            return null;
        var administrator = user.Role == UserRole.Administrator;
        var handler = administrator
            || competition.OwnerId == actorUserId
            || competition.ManagerIds.Contains(actorUserId)
            || competition.JudgeIds.Contains(actorUserId);
        var observer = !handler && competition.ObserverIds.Contains(actorUserId);
        var participant = await db.Teams.AsNoTracking().AnyAsync(team =>
            team.CompetitionId == competitionId
            && team.MemberIds.Contains(actorUserId)
            && team.DeletedAt == null
            && team.RegistrationStatus == TeamRegistrationStatus.Approved
            && !team.IsBanned,
            ct);
        return new(handler, observer, participant);
    }

    private async Task<CompetitionQuestionAccess?> ResolveAccessAsync(
        CompetitionQuestion question,
        Guid actorUserId,
        ActorContext actor,
        CancellationToken ct)
    {
        if (actor.IsCompetitionHandler)
            return CompetitionQuestionAccess.Handler;
        if (actor.IsObserver)
            return CompetitionQuestionAccess.Observer;
        if (question.AskedByUserId == actorUserId)
            return CompetitionQuestionAccess.Asker;
        if (question.CompetitionChallengeId is not null
            && await IsChallengeAuthorAsync(
                question.CompetitionChallengeId.Value,
                actorUserId,
                ct))
            return CompetitionQuestionAccess.Handler;
        return question.PublishedAt is not null && actor.IsApprovedParticipant
            ? CompetitionQuestionAccess.Public
            : null;
    }

    private Task<bool> IsChallengeAuthorAsync(
        Guid competitionChallengeId,
        Guid actorUserId,
        CancellationToken ct) =>
        db.CompetitionChallenges.AsNoTracking().AnyAsync(binding =>
            binding.Id == competitionChallengeId
            && db.Challenges.Any(challenge => challenge.Id == binding.ChallengeId
                && (challenge.OwnerId == actorUserId
                    || challenge.ManagerIds.Contains(actorUserId))),
            ct);

    private async Task<Guid?> ResolveTemplateChallengeIdAsync(
        Guid? competitionChallengeId,
        CancellationToken ct) =>
        competitionChallengeId is null
            ? null
            : await db.CompetitionChallenges.AsNoTracking()
                .Where(binding => binding.Id == competitionChallengeId)
                .Select(binding => (Guid?)binding.ChallengeId)
                .SingleOrDefaultAsync(ct);

    private async Task<Guid[]> ResolveHandlerRecipientIdsAsync(
        Guid competitionId,
        Guid? challengeId,
        Guid excludedUserId,
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
        var challenge = challengeId is null
            ? null
            : await db.Challenges.AsNoTracking()
                .Where(candidate => candidate.Id == challengeId)
                .Select(candidate => new { candidate.OwnerId, candidate.ManagerIds })
                .SingleOrDefaultAsync(ct);
        var challengeHandlers = challenge is null
            ? []
            : challenge.ManagerIds.Append(challenge.OwnerId).ToArray();
        var candidates = competition.ManagerIds
            .Concat(competition.JudgeIds)
            .Append(competition.OwnerId)
            .Concat(challengeHandlers)
            .Distinct()
            .Where(userId => userId != excludedUserId)
            .ToArray();
        return await db.Users.AsNoTracking()
            .Where(user => user.Id != excludedUserId
                && user.Kind == UserKind.Human
                && user.AccountStatus == UserAccountStatus.Active
                && (user.Role == UserRole.Administrator || candidates.Contains(user.Id)))
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
    }

    private async Task<CompetitionQuestionView> BuildViewAsync(
        CompetitionQuestion question,
        CompetitionQuestionAccess access,
        bool includeEntries,
        CancellationToken ct)
    {
        var isPublic = access == CompetitionQuestionAccess.Public;
        var teamName = isPublic
            ? null
            : await db.Teams.IgnoreQueryFilters().AsNoTracking()
                .Where(team => team.Id == question.TeamId)
                .Select(team => team.Name)
                .SingleOrDefaultAsync(ct);
        var actorIds = includeEntries
            ? question.Entries.Select(entry => entry.ActorUserId)
                .Append(question.AskedByUserId)
                .Distinct()
                .ToArray()
            : [question.AskedByUserId];
        var userNames = await db.Users.AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        var entries = includeEntries
            ? question.Entries
                .Where(entry => !isPublic
                    || entry.Kind == CompetitionQuestionEntryKind.Message
                    && entry.PublishedAt is not null)
                .OrderBy(entry => entry.CreatedAt)
                .ThenBy(entry => entry.Id)
                .Select(entry => new CompetitionQuestionEntryView(
                    entry.Id,
                    entry.Kind,
                    entry.ActorRole,
                    isPublic ? null : entry.ActorUserId,
                    entry.ActorRole == CompetitionQuestionParticipantRole.Asker && isPublic
                        ? "Anonymous participant"
                        : userNames.GetValueOrDefault(entry.ActorUserId, "Unknown user"),
                    entry.Body,
                    entry.FromStatus,
                    entry.ToStatus,
                    entry.TargetEntryId,
                    entry.PublishedAt,
                    entry.CreatedAt))
                .ToArray()
            : [];
        return new(
            question.Id,
            question.CompetitionId,
            question.CompetitionChallengeId,
            isPublic ? null : question.TeamId,
            isPublic ? null : question.AskedByUserId,
            isPublic
                ? "Anonymous participant"
                : userNames.GetValueOrDefault(question.AskedByUserId, "Unknown user"),
            teamName,
            isPublic ? null : question.SubmissionId,
            question.Subject,
            question.Title,
            question.Body,
            question.Status,
            access,
            question.Revision,
            question.PublishedAt,
            question.CreatedAt,
            question.UpdatedAt,
            entries);
    }

    private static CompetitionQuestionEntry StatusTransition(
        CompetitionQuestion question,
        Guid actorUserId,
        CompetitionQuestionParticipantRole actorRole,
        CompetitionQuestionStatus target,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            QuestionId = question.Id,
            Kind = CompetitionQuestionEntryKind.StatusTransition,
            ActorRole = actorRole,
            ActorUserId = actorUserId,
            FromStatus = question.Status,
            ToStatus = target,
            CreatedAt = now
        };

    private static CompetitionQuestionMutationResult Failure(
        CompetitionQuestionFailure failure) => new(null, failure);

    private sealed record ActorContext(
        bool IsCompetitionHandler,
        bool IsObserver,
        bool IsApprovedParticipant);
}
