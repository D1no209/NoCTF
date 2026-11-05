using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Application.Questionnaires;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Questionnaires.Activities;

public sealed class QuestionnaireReviewStore(NoCtfDbContext db, IPostCommitMessagePublisher publisher,
    NoCTF.Application.Questionnaires.Realtime.IQuestionnaireChangePublisher? signals = null) : IQuestionnaireReviewStore
{
    public async Task<QuestionnaireAuditPage?> AuditAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct)
    {
        var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanJudge) return null;
        var operations = db.Set<QuestionnaireActivityOperation>().AsNoTracking().Where(item => item.ActivityId == activityId);
        return new(await operations.OrderByDescending(item => item.OccurredAt).ThenBy(item => item.Id)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200)).Select(item => new QuestionnaireAuditRow(item.Id, item.Kind,
                db.Users.Where(user => user.Id == item.ActorId).Select(user => user.UserName).Single(), item.OccurredAt, item.Reason)).ToArrayAsync(ct),
            await operations.CountAsync(ct));
    }

    public async Task<QuestionnaireExamLeaderboard?> LeaderboardAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct)
    {
        var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || access.Activity.Kind != QuestionnaireActivityKind.Examination
            || access.Activity.State is QuestionnaireActivityState.Draft or QuestionnaireActivityState.Voided) return null;
        var participants = await db.QuestionnaireParticipants.AsNoTracking().Where(participant => participant.ActivityId == activityId)
            .Join(db.Users.AsNoTracking().Where(user => user.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active), participant => participant.UserId,
                user => user.Id, (participant, user) => new { participant.Id, UserId = user.Id, user.UserName }).ToArrayAsync(ct);
        var rows = await Rows(Sessions(activityId).Where(session => session.State == QuestionnaireSessionState.Submitted)).ToArrayAsync(ct);
        var ranks = participants.Select(participant =>
        {
            var score = QuestionnaireSessionGrading.Aggregate(access.Activity.ScoringMode, rows.Where(row => row.ParticipantId == participant.Id)
                .Select(row => new QuestionnaireSessionScore(row.SessionId, row.Attempt, row.Score!.Value, row.SubmittedAt!.Value)).ToArray());
            return new QuestionnaireExamRank(0, participant.UserId, participant.UserName, score.Points, score.AchievedAt, score.HasSubmission);
        }).OrderByDescending(row => row.Points).ThenBy(row => row.AchievedAt ?? DateTimeOffset.MaxValue).ThenBy(row => row.UserId)
            .Select((row, index) => row with { Rank = index + 1 }).ToArray();
        return new(ranks.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200)).ToArray(), ranks.Length);
    }

    public async Task<QuestionnaireReviewPage?> ListAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct)
    {
        var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanObserve) return null;
        var query = Sessions(activityId);
        var rows = await Rows(query.OrderByDescending(session => session.OpenedAt).ThenBy(session => session.Id)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200))).ToArrayAsync(ct);
        var participantIds = rows.Select(row => row.ParticipantId).Distinct().ToArray();
        var submitted = await Rows(query.Where(session => participantIds.Contains(session.ParticipantId) && session.State == QuestionnaireSessionState.Submitted)).ToArrayAsync(ct);
        return new(rows.Select(row => row with
        {
            Deadline = QuestionnaireClock.Session(row.OpenedAt, access.Activity.DurationSeconds, access.Deadline, access.Pauses, now),
            EffectiveScore = QuestionnaireSessionGrading.Aggregate(access.Activity.ScoringMode, submitted.Where(item => item.ParticipantId == row.ParticipantId)
                .Select(item => new QuestionnaireSessionScore(item.SessionId, item.Attempt, item.Score!.Value, item.SubmittedAt!.Value)).ToArray())
        }).ToArray(), await query.CountAsync(ct));
    }

    public async Task<QuestionnaireReviewSession?> ReadAsync(Guid actorId, Guid activityId, Guid sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanObserve) return null;
        var row = await Rows(Sessions(activityId).Where(session => session.Id == sessionId)).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        if (!access.CanJudge) return new(row, []);
        var session = await Sessions(activityId).AsNoTracking().Include(value => value.Items).ThenInclude(item => item.Options).SingleAsync(value => value.Id == sessionId, ct);
        QuestionnaireFactResults.Read(session);
        var ids = session.Items.Select(item => item.RevisionId).ToArray();
        var versions = await QuestionnaireContentReader.ReadAsync(db, ids, ct);
        var questions = ReadQuestionnaireSession.Project(session, versions, includeIssuedContent: true).Questions;
        var corrections = await db.Set<QuestionnaireGradingOverride>().AsNoTracking().Include(item => item.Options)
            .Where(item => item.ActivityId == activityId && ids.Contains(item.RevisionId)).ToDictionaryAsync(item => item.RevisionId, ct);
        return new(row, questions.Select(question =>
        {
            var item = session.Items.Single(item => item.Id == question.Id);
            var version = versions[item.RevisionId];
            var correct = corrections.TryGetValue(item.RevisionId, out var rule)
                ? rule.Options.Select(option => option.OptionId).ToArray() : version.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToArray();
            return new QuestionnaireReviewQuestion(question, item.RevisionId, correct, version.Explanation, item.Result);
        }).ToArray());
    }

    public async Task<QuestionnaireCorrectionResult> PreviewAsync(Guid actorId, Guid activityId, QuestionnaireCorrectionInput input, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        return await PreviewCoreAsync(actorId, activityId, input, now.AddMinutes(5), now, ct);
    }

    private async Task<QuestionnaireCorrectionResult> PreviewCoreAsync(Guid actorId, Guid activityId, QuestionnaireCorrectionInput input,
        DateTimeOffset expires, DateTimeOffset now, CancellationToken ct)
    {
        var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, actorId, now, ct, includeCandidates: true);
        if (access is null || !access.CanManage || !access.Activity.Rules.Any(rule => rule.Candidates.Any(candidate => candidate.RevisionId == input.RevisionId)))
            return new(Failure: QuestionnaireReviewFailure.NotFoundOrForbidden);
        var question = (await QuestionnaireContentReader.ReadAsync(db, [input.RevisionId], ct))[input.RevisionId];
        if (!QuestionnaireCorrectionPolicy.IsValid(question, input.Mode, input.CorrectOptions)) return new(Failure: QuestionnaireReviewFailure.InvalidCorrection);
        var allSessions = await Sessions(activityId).AsNoTracking().Where(session => session.State == QuestionnaireSessionState.Submitted)
            .Include(session => session.Items).ThenInclude(item => item.Options).ToArrayAsync(ct);
        var sessions = allSessions.Where(session => session.Items.Any(item => item.RevisionId == input.RevisionId)).ToArray();
        foreach (var session in allSessions) QuestionnaireFactResults.Read(session);
        long Effective(Guid participantId) => QuestionnaireSessionGrading.Aggregate(access.Activity.ScoringMode,
            allSessions.Where(session => session.ParticipantId == participantId).Select(session => new QuestionnaireSessionScore(session.Id, session.Attempt,
                QuestionnaireSessionGrading.Points(session), session.SubmittedAt!.Value)).ToArray()).Points;
        var beforeScores = sessions.Select(session => session.ParticipantId).Distinct().ToDictionary(id => id, Effective);
        var names = (await Rows(Sessions(activityId)).ToArrayAsync(ct)).GroupBy(row => row.ParticipantId).ToDictionary(group => group.Key, group => group.First().Name);
        var policy = new QuestionnaireGradingOverride { RevisionId = input.RevisionId, Mode = input.Mode,
            Options = input.CorrectOptions.Select(id => new QuestionnaireGradingOption { OptionId = id }).ToList() };
        var impacts = sessions.Select(session =>
        {
            var before = QuestionnaireSessionGrading.Points(session);
            QuestionnaireCorrectionPolicy.Apply(session.Items.Single(item => item.RevisionId == input.RevisionId), policy);
            return new QuestionnaireScoreImpact(session.Id, session.ParticipantId, before, QuestionnaireSessionGrading.Points(session));
        }).ToArray();
        var signature = $"{actorId:N}:{activityId:N}:{input.RevisionId:N}:{input.Mode}:{expires.UtcTicks}:{access.Activity.ConcurrencyStamp:N}:"
            + string.Join(',', input.CorrectOptions.Order()) + ":" + string.Join(';', allSessions.OrderBy(session => session.Id).Select(session => $"{session.Id:N}:{session.ConcurrencyStamp:N}"));
        var token = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(signature)).AsSpan(0, 16));
        return new(new(token, expires, sessions.Length, impacts)
        { Participants = beforeScores.Select(pair => new QuestionnaireParticipantScoreImpact(pair.Key, names[pair.Key], pair.Value, Effective(pair.Key))).ToArray() });
    }

    public async Task<QuestionnaireCorrectionResult> CorrectAsync(Guid actorId, Guid activityId, QuestionnaireCorrectionInput input, Guid token,
        DateTimeOffset expiresAt, string reason, DateTimeOffset now, CancellationToken ct)
    {
        try { return await CorrectCoreAsync(actorId, activityId, input, token, expiresAt, reason, now, ct); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is DbException { SqlState: "40001" or "40P01" or "23505" })
        { db.ChangeTracker.Clear(); return new(Failure: QuestionnaireReviewFailure.Conflict); }
    }

    private async Task<QuestionnaireCorrectionResult> CorrectCoreAsync(Guid actorId, Guid activityId, QuestionnaireCorrectionInput input, Guid token,
        DateTimeOffset expiresAt, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) return new(Failure: QuestionnaireReviewFailure.ReasonRequired);
        if (expiresAt <= now || expiresAt > now.AddMinutes(5)) return new(Failure: QuestionnaireReviewFailure.PreviewChanged);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var preview = await PreviewCoreAsync(actorId, activityId, input, expiresAt, now, ct);
        if (preview.Failure is not null) return preview;
        if (preview.Preview!.Token != token) return new(Failure: QuestionnaireReviewFailure.PreviewChanged);
        var rule = await db.Set<QuestionnaireGradingOverride>().Include(item => item.Options)
            .SingleOrDefaultAsync(item => item.ActivityId == activityId && item.RevisionId == input.RevisionId, ct);
        if (rule is null) { rule = new() { ActivityId = activityId, RevisionId = input.RevisionId }; db.Add(rule); }
        else { db.RemoveRange(rule.Options); rule.Options.Clear(); await db.SaveChangesAsync(ct); }
        rule.TargetRevision = Guid.NewGuid(); rule.Mode = input.Mode;
        rule.Options = input.CorrectOptions.Select(id => new QuestionnaireGradingOption { OptionId = id }).ToList();
        var activity = await db.QuestionnaireActivities.SingleAsync(item => item.Id == activityId, ct);
        db.Entry(activity).Property(item => item.ConcurrencyStamp).IsModified = true;
        db.Add(new QuestionnaireActivityOperation { Id = Guid.NewGuid(), ActivityId = activityId, ActorId = actorId, RevisionId = input.RevisionId,
            Kind = input.Mode == QuestionnaireCorrectionMode.FullCredit ? QuestionnaireActivityOperationKind.FullCreditGranted : QuestionnaireActivityOperationKind.AnswerCorrected,
            OccurredAt = now, Reason = reason });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await publisher.PublishAsync(new ApplyQuestionnaireCorrection(activityId, input.RevisionId, rule.TargetRevision));
        await publisher.FlushCommittedMessagesAsync();
        return new(TargetRevision: rule.TargetRevision);
    }

    public async Task ApplyAsync(ApplyQuestionnaireCorrection message, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var rule = await db.Set<QuestionnaireGradingOverride>().Include(item => item.Options)
            .SingleOrDefaultAsync(item => item.ActivityId == message.ActivityId && item.RevisionId == message.RevisionId, ct);
        if (rule is null || rule.TargetRevision != message.TargetRevision || rule.AppliedRevision == rule.TargetRevision) return;
        var sessions = await Sessions(message.ActivityId).Where(session => session.State == QuestionnaireSessionState.Submitted
            && session.Items.Any(item => item.RevisionId == message.RevisionId && item.AppliedGradingRevision != message.TargetRevision))
            .OrderBy(session => session.Id).Take(100).Include(session => session.Items).ThenInclude(item => item.Options).ToArrayAsync(ct);
        foreach (var session in sessions)
        {
            QuestionnaireFactResults.Read(session);
            QuestionnaireCorrectionPolicy.Apply(session.Items.Single(item => item.RevisionId == message.RevisionId), rule);
            session.ConcurrencyStamp = Guid.NewGuid();
        }
        db.Entry(rule).Property(item => item.ConcurrencyStamp).IsModified = true;
        if (sessions.Length < 100) rule.AppliedRevision = rule.TargetRevision;
        foreach (var session in sessions) QuestionnaireFactResults.Write(session, now);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await NotifyAsync(message.ActivityId, ct);
        if (sessions.Length == 100) { await publisher.PublishAsync(message); await publisher.FlushCommittedMessagesAsync(); }
    }

    public async Task<QuestionnaireReviewFailure?> VoidSessionAsync(Guid actorId, Guid activityId, Guid sessionId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) return QuestionnaireReviewFailure.ReasonRequired;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanManage) return QuestionnaireReviewFailure.NotFoundOrForbidden;
        var session = await Sessions(activityId).SingleOrDefaultAsync(value => value.Id == sessionId, ct);
        if (session is null) return QuestionnaireReviewFailure.NotFoundOrForbidden;
        if (session.State == QuestionnaireSessionState.Voided) return null;
        session.State = QuestionnaireSessionState.Voided;
        var slot = await db.Set<QuestionnaireActiveSession>().SingleOrDefaultAsync(value => value.SessionId == session.Id, ct);
        if (slot is not null) db.Remove(slot);
        db.Add(new QuestionnaireActivityOperation { Id = Guid.NewGuid(), ActivityId = activityId, ActorId = actorId, SessionId = session.Id,
            Kind = QuestionnaireActivityOperationKind.SessionVoided, OccurredAt = now, Reason = reason });
        QuestionnaireFactResults.Write(session, now); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await NotifyAsync(activityId, ct);
        return null;
    }

    private async Task NotifyAsync(Guid activityId, CancellationToken ct)
    {
        if (signals is not null) await signals.PublishAsync(new(activityId, null), ct);
    }

    private IQueryable<QuestionnaireSession> Sessions(Guid activityId) => db.QuestionnaireSessions
        .Include(session => session.Assignments).ThenInclude(assignment => assignment.Fact).ThenInclude(fact => fact!.CapabilityOutcomes)
        .Where(session => db.QuestionnaireParticipants.Any(participant => participant.Id == session.ParticipantId && participant.ActivityId == activityId));

    private IQueryable<QuestionnaireReviewRow> Rows(IQueryable<QuestionnaireSession> sessions) => sessions.AsNoTracking()
        .Join(db.QuestionnaireParticipants.AsNoTracking(), session => session.ParticipantId, participant => participant.Id, (session, participant) => new QuestionnaireReviewRow(
            session.Id, participant.Id, participant.UserId,
            db.Users.Where(user => user.Id == participant.UserId).Select(user => user.UserName).Single(),
            session.Attempt, session.State, session.OpenedAt, session.SubmittedAt,
            session.State == QuestionnaireSessionState.Submitted ? session.Items.Where(item => db.GameplayFacts.Any(fact => fact.QuestionnaireAssignmentId == item.AssignmentId
                && fact.State == GameplayFactState.Completed && fact.Result == GameplayFactResult.Applied
                && fact.CapabilityOutcomes.OfType<QuestionnaireOutcome>().Any(outcome => outcome.Questions.Any(result => result.QuestionId == item.QuestionnaireQuestionId
                    && (result.Result == QuestionnaireAnswerResult.Correct || result.Result == QuestionnaireAnswerResult.FullCredit)))))
                .Sum(item => (long?)item.Points) ?? 0 : null)
            { QuestionCount = session.Items.Count, AnsweredCount = session.Items.Count(item => item.Options.Any(option => option.Selected)), SubmissionKind = session.SubmissionKind });
}
