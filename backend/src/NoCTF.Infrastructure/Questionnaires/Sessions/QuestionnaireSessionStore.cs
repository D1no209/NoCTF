using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Application.Questionnaires.Realtime;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Application.Questionnaires;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Questionnaires.Activities;

namespace NoCTF.Infrastructure.Questionnaires.Sessions;

public sealed class QuestionnaireSessionStore(NoCtfDbContext db, IQuestionnaireChangePublisher? signals = null) : IQuestionnaireSessionStore
{
    private readonly HashSet<QuestionnaireChanged> changes = [];
    public Task<QuestionnaireSessionResult> ReadAsync(QuestionnaireSessionCommand command, CancellationToken ct)
        => ExecuteAsync(command, SessionAction.Read, null, ct);
    public Task<QuestionnaireSessionResult> IssueAsync(QuestionnaireSessionCommand command, CancellationToken ct)
        => ExecuteAsync(command, SessionAction.Issue, null, ct);
    public Task<QuestionnaireSessionResult> SubmitAsync(QuestionnaireSessionCommand command, CancellationToken ct)
        => ExecuteAsync(command, SessionAction.Submit, null, ct);
    public Task<QuestionnaireSessionResult> AnswerAsync(QuestionnaireAnswerCommand command, CancellationToken ct)
        => ExecuteAsync(new(command.ActorId, command.ActivityId, command.SessionId, null, command.Now), SessionAction.Answer, command, ct);

    private async Task<QuestionnaireSessionResult> ExecuteAsync(QuestionnaireSessionCommand command, SessionAction action, QuestionnaireAnswerCommand? answer, CancellationToken ct)
    {
        QuestionnaireSessionResult? result = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            changes.Clear();
            try { result = await ExecuteOnceAsync(command, action, answer, ct); break; }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException
                || exception.GetBaseException() is DbException { SqlState: "40001" or "40P01" or "23505" })
            {
                db.ChangeTracker.Clear();
                if (attempt == 5) return new(Failure: action == SessionAction.Answer ? QuestionnaireSessionFailure.AnswerConflict : QuestionnaireSessionFailure.SessionConflict);
                await Task.Delay(RandomNumberGenerator.GetInt32(5, 25) * (attempt + 1), ct);
            }
        }
        if (result is null) throw new InvalidOperationException("Questionnaire transaction retries exhausted.");
        if (result.Failure is null) await NotifyCommittedAsync(ct);
        return result;
    }

    private async Task<QuestionnaireSessionResult> ExecuteOnceAsync(QuestionnaireSessionCommand command, SessionAction action, QuestionnaireAnswerCommand? answer, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await QuestionnaireActivityAccess.ReadAsync(db, command.ActivityId, command.ActorId, command.Now, ct, includeCandidates: action == SessionAction.Issue);
        if (access is null || !access.CanParticipate || access.Activity.State == QuestionnaireActivityState.Draft)
            return new(Failure: QuestionnaireSessionFailure.NotFoundOrForbidden);
        var activity = access.Activity;
        if (action == SessionAction.Issue && (command.RequestId is null || command.RequestId == Guid.Empty))
            return new(Failure: QuestionnaireSessionFailure.InvalidRequestIdentity);
        if (activity.State == QuestionnaireActivityState.Voided) return new(Failure: QuestionnaireSessionFailure.Voided);
        var participant = await db.QuestionnaireParticipants.SingleOrDefaultAsync(item => item.ActivityId == activity.Id
            && item.UserId == command.ActorId, ct);
        QuestionnaireSession? session = null;
        if (participant is not null)
        {
            session = command.SessionId is { } sessionId
                ? await Sessions().SingleOrDefaultAsync(item => item.Id == sessionId && item.ParticipantId == participant.Id, ct)
                : await Sessions().Where(item => item.ParticipantId == participant.Id).OrderByDescending(item => item.Attempt).FirstOrDefaultAsync(ct);
            if (action == SessionAction.Issue && await db.Set<QuestionnaireIssueReceipt>().AsNoTracking().SingleOrDefaultAsync(item => item.ParticipantId == participant.Id
                    && item.RequestId == command.RequestId, ct) is { } issueReceipt)
            {
                var original = await Sessions().SingleAsync(item => item.Id == issueReceipt.SessionId, ct);
                QuestionnaireFactResults.Read(original);
                var originalDeadline = QuestionnaireClock.Session(original.OpenedAt, activity.DurationSeconds, access.Deadline, access.Pauses, command.Now);
                await ExpireAsync(original, activity.Id, originalDeadline, command.Now, activity.State == QuestionnaireActivityState.Finished, ct);
                await EnsureFactAsync(access, participant, original, command.Now, ct);
                await db.SaveChangesAsync(ct);
                var originalView = await ViewAsync(access, participant, original, originalDeadline, command.Now, ct);
                await transaction.CommitAsync(ct);
                return new(originalView);
            }
        }
        if (command.SessionId is not null && session is null) return new(Failure: QuestionnaireSessionFailure.NotFoundOrForbidden);
        if (session is not null) QuestionnaireFactResults.Read(session);
        var deadline = session is null ? access.Deadline : QuestionnaireClock.Session(session.OpenedAt, activity.DurationSeconds, access.Deadline, access.Pauses, command.Now);
        if (session is { State: QuestionnaireSessionState.Active } && command.Now >= deadline.At)
        {
            await ExpireAsync(session, activity.Id, deadline, command.Now, activity.State == QuestionnaireActivityState.Finished, ct);
            await db.SaveChangesAsync(ct);
        }
        if (action == SessionAction.Issue && session?.State != QuestionnaireSessionState.Active)
        {
            if (activity.State != QuestionnaireActivityState.Published || command.Now >= access.Deadline.At) return new(Failure: QuestionnaireSessionFailure.DeadlineReached);
            if (command.Now < activity.OpensAt) return new(Failure: QuestionnaireSessionFailure.NotOpen);
            if (access.Deadline.Paused) return new(Failure: QuestionnaireSessionFailure.Paused);
            if (activity.IssuanceStopped) return new(Failure: QuestionnaireSessionFailure.IssuanceStopped);
            participant ??= new() { Id = Guid.NewGuid(), ActivityId = activity.Id, UserId = command.ActorId };
            if (activity.AttemptLimit is { } limit && participant.IssuedCount >= limit) return new(Failure: QuestionnaireSessionFailure.AttemptsExhausted);
            if (participant.IssuedCount != command.ExpectedIssuedCount) return new(Failure: QuestionnaireSessionFailure.SessionConflict);
            var used = await db.Set<QuestionnaireDrawReceipt>().Where(receipt => receipt.ParticipantId == participant.Id).Select(receipt => receipt.SourceId).ToArrayAsync(ct);
            var allocation = QuestionnaireSessionAllocator.Allocate(activity.Rules,
                activity.SelectionKind == QuestionnaireSelectionKind.Fixed ? [] : used.ToHashSet(),
                activity.SelectionKind == QuestionnaireSelectionKind.Fixed ? 1 : activity.AttemptLimit is { } attempts ? attempts - participant.IssuedCount : 1, ct);
            if (!allocation.Succeeded) return new(Failure: QuestionnaireSessionFailure.InsufficientQuestions, Shortages: allocation.Shortages);
            var assigned = allocation.Questions.Where(item => item.Attempt == 0).ToArray();
            var sources = assigned.Select(item => item.SourceId).ToArray();
            // 已发布的练习不能泄露后来被考试保留的题目。
            if (activity.Kind == QuestionnaireActivityKind.Practice
                && (await QuestionnaireReservations.ReadAsync(db, sources, activity.Id, command.Now, ct)).Count > 0)
                return new(Failure: QuestionnaireSessionFailure.IssuanceStopped);
            if (db.Entry(participant).State == EntityState.Detached) db.QuestionnaireParticipants.Add(participant);
            participant.IssuedCount++;
            session = new() { Id = Guid.CreateVersion7(command.Now), ParticipantId = participant.Id, Attempt = participant.IssuedCount,
                OpenedAt = command.Now };
            var versions = await VersionsAsync(assigned.Select(item => item.RevisionId).ToArray(), ct);
            var assignments = versions.Values.GroupBy(version => version.RevisionId).ToDictionary(group => group.Key,
                group => new QuestionnaireAssignment { Id = Guid.NewGuid(), SessionId = session.Id, RevisionId = group.Key, ChallengeId = group.First().Revision.ChallengeId });
            session.Assignments = assignments.Values.ToList();
            foreach (var assignedQuestion in assigned)
            {
                var version = versions[assignedQuestion.RevisionId];
                var options = version.Options.Select(option => option.Id).ToArray();
                QuestionnaireSessionAllocator.Shuffle(options);
                session.Items.Add(new() { Id = Guid.NewGuid(), RevisionId = assignedQuestion.RevisionId, RuleId = assignedQuestion.RuleId,
                    AssignmentId = assignments[version.RevisionId].Id, QuestionnaireRevisionId = version.RevisionId,
                    QuestionnaireQuestionId = version.Id, Order = session.Items.Count, Points = assignedQuestion.Points,
                    Options = options.Select((id, order) => new QuestionnaireSessionOption { OptionId = id, QuestionVersionId = version.VersionId, Order = order }).ToList() });
                if (!used.Contains(assignedQuestion.SourceId))
                    db.Add(new QuestionnaireDrawReceipt { ParticipantId = participant.Id, SessionId = session.Id, SourceId = assignedQuestion.SourceId, IssuedAt = command.Now });
            }
            db.QuestionnaireSessions.Add(session);
            db.Add(new QuestionnaireActiveSession { ParticipantId = participant.Id, SessionId = session.Id });
            var exposed = await db.Set<QuestionnaireQuestionExposure>().Where(item => item.ActivityId == activity.Id && sources.Contains(item.SourceId)).Select(item => item.SourceId).ToArrayAsync(ct);
            var sourceRows = await db.Set<QuestionnaireQuestionSource>().Where(source => sources.Contains(source.Id)).ToArrayAsync(ct);
            foreach (var source in sourceRows)
                if (source.FirstExposedAt is null || source.FirstExposedAt > command.Now) source.FirstExposedAt = command.Now;
            foreach (var source in sources.Except(exposed)) db.Add(new QuestionnaireQuestionExposure { SourceId = source, ActivityId = activity.Id, FirstExposedAt = command.Now });
            await db.SaveChangesAsync(ct);
            deadline = QuestionnaireClock.Session(session.OpenedAt, activity.DurationSeconds, access.Deadline, access.Pauses, command.Now);
            changes.Add(new(activity.Id, participant.Id));
        }
        else if (action == SessionAction.Answer)
        {
            if (session is null || answer is null) return new(Failure: QuestionnaireSessionFailure.NotFoundOrForbidden);
            if (answer.RequestId == Guid.Empty) return new(Failure: QuestionnaireSessionFailure.InvalidRequestIdentity);
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", answer.OptionIds.Order().Select(id => id.ToString("N")))));
            var receipt = await db.Set<QuestionnaireAnswerReceipt>().SingleOrDefaultAsync(item => item.ItemId == answer.ItemId && item.RequestId == answer.RequestId, ct);
            if (receipt is not null)
            {
                if (receipt.ActorId != command.ActorId || !CryptographicOperations.FixedTimeEquals(receipt.SelectionDigest, digest))
                    return new(Failure: QuestionnaireSessionFailure.InvalidRequestIdentity);
            }
            else
            {
                var item = session.Items.SingleOrDefault(item => item.Id == answer.ItemId);
                if (item is null) return new(Failure: QuestionnaireSessionFailure.NotFoundOrForbidden);
                var question = (await VersionsAsync([item.RevisionId], ct))[item.RevisionId];
                var failure = QuestionnaireSessionWorkflow.SaveAnswer(session, question, deadline,
                    new(answer.ItemId, answer.ExpectedRevision, answer.OptionIds, command.ActorId, command.Now));
                if (failure is not null) return new(Failure: failure);
                await db.SaveChangesAsync(ct);
                db.Add(new QuestionnaireAnswerReceipt { ItemId = item.Id, RequestId = answer.RequestId, ActorId = command.ActorId,
                    ResultingRevision = item.ConcurrencyStamp, SavedAt = command.Now, SelectionDigest = digest });
                await db.SaveChangesAsync(ct);
                changes.Add(new(activity.Id, session.ParticipantId));
            }
        }
        else if (action == SessionAction.Submit)
        {
            if (session is null) return new(Failure: QuestionnaireSessionFailure.NotFoundOrForbidden);
            var wasActive = session.State == QuestionnaireSessionState.Active;
            var failure = QuestionnaireSessionWorkflow.Submit(session, await VersionsAsync(session.Items.Select(item => item.RevisionId).ToArray(), ct),
                deadline, new(command.ExpectedRevision ?? Guid.Empty, command.ActorId, command.Now));
            if (failure is not null) return new(Failure: failure);
            if (wasActive) foreach (var assignment in session.Assignments) db.GameplayFacts.Add(assignment.Fact!);
            var active = await db.Set<QuestionnaireActiveSession>().SingleOrDefaultAsync(slot => slot.SessionId == session.Id, ct);
            if (active is not null) db.Remove(active);
            await db.SaveChangesAsync(ct);
            if (wasActive) changes.Add(new(activity.Id, session.ParticipantId));
        }
        if (action == SessionAction.Issue && session is not null && participant is not null)
        {
            db.Add(new QuestionnaireIssueReceipt { ParticipantId = participant.Id, SessionId = session.Id, RequestId = command.RequestId!.Value });
            await db.SaveChangesAsync(ct);
        }
        if (session is not null && participant is not null)
        {
            await EnsureFactAsync(access, participant, session, command.Now, ct);
            await db.SaveChangesAsync(ct);
        }
        var view = await ViewAsync(access, participant, session, deadline, command.Now, ct);
        await transaction.CommitAsync(ct);
        return new(view);
    }

    private async Task<QuestionnaireSessionView> ViewAsync(QuestionnaireAccess access, QuestionnaireParticipant? participant,
        QuestionnaireSession? session, QuestionnaireDeadline deadline, DateTimeOffset now, CancellationToken ct)
    {
        var historicalSessions = participant is null ? [] : await Sessions().AsNoTracking()
            .Where(value => value.ParticipantId == participant.Id && value.State != QuestionnaireSessionState.Active).OrderBy(value => value.Attempt)
            .AsSplitQuery().ToArrayAsync(ct);
        foreach (var historical in historicalSessions) QuestionnaireFactResults.Read(historical);
        var history = historicalSessions.Select(historical => new QuestionnaireSessionHistory(historical.Id, historical.Attempt, historical.State,
            historical.State == QuestionnaireSessionState.Submitted ? QuestionnaireSessionGrading.Points(historical) : null, historical.SubmittedAt)).ToArray();
        var actorIds = session?.State == QuestionnaireSessionState.Active
            ? session.Items.Where(item => item.AnsweredById.HasValue).Select(item => item.AnsweredById!.Value).Distinct().ToArray() : [];
        var actors = actorIds.Length == 0 ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking().Where(user => actorIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        return new(QuestionnaireActivityMapping.Summary(access.Activity), deadline,
            session is null ? null : ReadQuestionnaireSession.Project(session, session.State == QuestionnaireSessionState.Active
                ? await VersionsAsync(session.Items.Select(item => item.RevisionId).ToArray(), ct) : new Dictionary<Guid, QuestionnaireQuestion>(), actors),
            participant?.IssuedCount ?? 0, QuestionnaireSessionGrading.Aggregate(access.Activity.ScoringMode,
                history.Where(item => item.State == QuestionnaireSessionState.Submitted)
                    .Select(item => new QuestionnaireSessionScore(item.SessionId, item.Attempt, item.Score!.Value, item.SubmittedAt!.Value)).ToArray()), history)
        {
            ServerTime = now,
            CanAnswer = session?.State == QuestionnaireSessionState.Active && now < deadline.At && !deadline.Paused
                && access.Activity.State == QuestionnaireActivityState.Published,
            CanIssue = session?.State != QuestionnaireSessionState.Active && access.Activity.State == QuestionnaireActivityState.Published
                && !access.Activity.IssuanceStopped && now >= access.Activity.OpensAt && now < access.Deadline.At && !access.Deadline.Paused
                && (access.Activity.AttemptLimit is null || (participant?.IssuedCount ?? 0) < access.Activity.AttemptLimit)
        };
    }

    public async Task SweepAsync(Guid activityId, DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.QuestionnaireSessions.AsNoTracking().Where(session => session.State == QuestionnaireSessionState.Active
            && db.QuestionnaireParticipants.Any(participant => participant.Id == session.ParticipantId && participant.ActivityId == activityId))
            .Select(session => session.Id).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            for (var retry = 0; retry < 3; retry++)
            {
                changes.Clear();
                try
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                    var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, null, now, ct);
                    if (access is null) break;
                    var session = await Sessions().SingleAsync(item => item.Id == id, ct);
                    QuestionnaireFactResults.Read(session);
                    var deadline = QuestionnaireClock.Session(session.OpenedAt, access.Activity.DurationSeconds, access.Deadline, access.Pauses, now);
                    await ExpireAsync(session, activityId, deadline, now, access.Activity.FinishedAt is not null, ct);
                    var participant = await db.QuestionnaireParticipants.SingleAsync(item => item.Id == session.ParticipantId, ct);
                    await EnsureFactAsync(access, participant, session, now, ct);
                    await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                    break;
                }
                catch (Exception exception) when (retry < 2 && TransactionFailureClassifier.IsRetryable(exception)) { }
                finally { db.ChangeTracker.Clear(); }
            }
            await NotifyCommittedAsync(ct);
        }
    }

    private async Task EnsureFactAsync(QuestionnaireAccess access, QuestionnaireParticipant participant, QuestionnaireSession session,
        DateTimeOffset now, CancellationToken ct)
    {
        if (session.State != QuestionnaireSessionState.Submitted) return;
        var ids = session.Items.Select(item => item.RevisionId).ToArray();
        var overrides = await db.Set<QuestionnaireGradingOverride>().AsNoTracking().Include(item => item.Options)
            .Where(item => item.ActivityId == access.Activity.Id && ids.Contains(item.RevisionId)).ToDictionaryAsync(item => item.RevisionId, ct);
        var changed = false;
        foreach (var item in session.Items)
            if (overrides.TryGetValue(item.RevisionId, out var correction) && item.AppliedGradingRevision != correction.TargetRevision)
            { QuestionnaireCorrectionPolicy.Apply(item, correction); changed = true; }
        if (changed) { session.ConcurrencyStamp = Guid.NewGuid(); changes.Add(new(access.Activity.Id, participant.Id)); }
        QuestionnaireFactResults.Write(session, now);
    }

    private async Task NotifyCommittedAsync(CancellationToken ct)
    {
        if (signals is not null) foreach (var change in changes) await signals.PublishAsync(change, ct);
        changes.Clear();
    }

    private async Task ExpireAsync(QuestionnaireSession session, Guid activityId, QuestionnaireDeadline deadline, DateTimeOffset now, bool finished, CancellationToken ct)
    {
        if (!QuestionnaireSessionWorkflow.Expire(session, await VersionsAsync(session.Items.Select(item => item.RevisionId).ToArray(), ct), deadline, now, finished)) return;
        foreach (var assignment in session.Assignments) db.GameplayFacts.Add(assignment.Fact!);
        changes.Add(new(activityId, session.ParticipantId));
        var active = await db.Set<QuestionnaireActiveSession>().SingleOrDefaultAsync(slot => slot.SessionId == session.Id, ct);
        if (active is not null) db.Remove(active);
    }
    private IQueryable<QuestionnaireSession> Sessions() => db.QuestionnaireSessions.Include(session => session.Items).ThenInclude(item => item.Options)
        .Include(session => session.Assignments).ThenInclude(assignment => assignment.Fact).ThenInclude(fact => fact!.CapabilityOutcomes);
    private Task<Dictionary<Guid, QuestionnaireQuestion>> VersionsAsync(Guid[] ids, CancellationToken ct) => QuestionnaireContentReader.ReadAsync(db, ids, ct);
    private enum SessionAction { Read, Issue, Answer, Submit }
}
