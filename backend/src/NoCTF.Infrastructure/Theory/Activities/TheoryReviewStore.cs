using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Theory.Activities;

public sealed class TheoryReviewStore(NoCtfDbContext db, IPostCommitMessagePublisher publisher, ILeaderboardCache leaderboard,
    NoCTF.Application.Theory.Realtime.ITheoryChangePublisher? signals = null) : ITheoryReviewStore
{
    public async Task<Guid?> StartConsultationAsync(Guid actorId, Guid activityId, Guid paperId, string body, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 2000) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access?.Competition is not { } competition || !access.CanJudge) return null;
        var paper = await Rows(Papers(activityId).Where(item => item.Id == paperId)).SingleOrDefaultAsync(ct);
        if (paper?.TeamId is not { } teamId) return null;
        var factId = await db.GameplayFacts.Where(item => item.TheoryPaperId == paperId).Select(item => (Guid?)item.Id).SingleOrDefaultAsync(ct);
        var prior = await db.Notifications.Where(item => item.Kind == NotificationKind.QuestionOpened && item.TeamId == teamId
            && item.SourceId == actorId && item.RelatedId == competition.Id && item.Body == body && item.SentAt >= now.AddMinutes(-1))
            .Select(item => (Guid?)item.Id).FirstOrDefaultAsync(ct);
        if (prior is not null) return prior;
        var title = $"{access.Activity.Name} · #{paper.Attempt}";
        var notification = new QuestionOpenedNotification
        {
            Id = Guid.CreateVersion7(now), SourceType = NotificationSourceType.User, SourceId = actorId,
            TargetType = NotificationTargetType.CompetitionCollaborators, TargetId = competition.Id,
            QuestionSubject = CompetitionQuestionSubject.Platform, Title = title[..Math.Min(title.Length, 160)], Body = body.Trim(),
            TeamId = teamId, GameplayFactId = factId, QuestionStatus = CompetitionQuestionStatus.Replied,
            QuestionActorRole = access.CanManage ? CompetitionQuestionParticipantRole.CompetitionManager : CompetitionQuestionParticipantRole.Judge,
            SentAt = now, RelatedType = EntityReferenceKind.Competition, RelatedId = competition.Id
        };
        db.Notifications.Add(notification);
        var recipients = await db.Teams.Where(team => team.Id == teamId).SelectMany(team => team.Members)
            .Where(member => member.ActiveMembership != null).Select(member => member.UserId).ToArrayAsync(ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await publisher.PublishAsync(new DeliverCompetitionQuestionNotification(competition.Id, notification.Id, null, recipients,
            NotificationKind.QuestionOpened, CompetitionQuestionNotificationEvent.Opened, notification.Title, now));
        await publisher.FlushCommittedMessagesAsync();
        return notification.Id;
    }
    public async Task<TheoryAuditPage?> AuditAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct)
    {
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanObserve) return null;
        var operations = db.Set<TheoryActivityOperation>().AsNoTracking().Where(item => item.ActivityId == activityId);
        return new(await operations.OrderByDescending(item => item.OccurredAt).ThenBy(item => item.Id)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200)).Select(item => new TheoryAuditRow(item.Id, item.Kind,
                db.Users.Where(user => user.Id == item.ActorId).Select(user => user.UserName).Single(), item.OccurredAt, item.Reason)).ToArrayAsync(ct),
            await operations.CountAsync(ct));
    }
    public async Task<TheoryExamLeaderboard?> LeaderboardAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct)
    {
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || access.Activity.Kind != TheoryActivityKind.Examination
            || access.Activity.State is TheoryActivityState.Draft or TheoryActivityState.Voided) return null;
        var participants = await db.TheoryParticipants.AsNoTracking().Where(participant => participant.ActivityId == activityId && participant.UserId != null)
            .Join(db.Users.AsNoTracking().Where(user => user.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active), participant => participant.UserId,
                user => (Guid?)user.Id, (participant, user) => new { participant.Id, UserId = user.Id, user.UserName }).ToArrayAsync(ct);
        var rows = await Rows(Papers(activityId).Where(paper => paper.State == TheoryPaperState.Submitted)).ToArrayAsync(ct);
        var ranks = participants.Select(participant =>
        {
            var score = TheoryPaperGrading.Aggregate(access.Activity.ScoringMode, rows.Where(row => row.ParticipantId == participant.Id)
                .Select(row => new TheoryPaperScore(row.PaperId, row.Attempt, row.Score!.Value, row.SubmittedAt!.Value)).ToArray());
            return new TheoryExamRank(0, participant.UserId, participant.UserName, score.Points, score.AchievedAt, score.HasSubmission);
        }).OrderByDescending(row => row.Points).ThenBy(row => row.AchievedAt ?? DateTimeOffset.MaxValue).ThenBy(row => row.UserId)
            .Select((row, index) => row with { Rank = index + 1 }).ToArray();
        return new(ranks.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200)).ToArray(), ranks.Length);
    }
    public async Task<TheoryReviewPage?> ListAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct)
    {
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanObserve) return null;
        var query = Papers(activityId);
        var rows = await Rows(query.OrderByDescending(paper => paper.OpenedAt).ThenBy(paper => paper.Id)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200))).ToArrayAsync(ct);
        var participantIds = rows.Select(row => row.ParticipantId).Distinct().ToArray();
        var submitted = await Rows(query.Where(paper => participantIds.Contains(paper.ParticipantId) && paper.State == TheoryPaperState.Submitted)).ToArrayAsync(ct);
        return new(rows.Select(row => row with
        {
            Deadline = TheoryClock.Paper(row.OpenedAt, access.Activity.DurationSeconds, access.Deadline, access.Pauses, now),
            EffectiveScore = TheoryPaperGrading.Aggregate(access.Activity.ScoringMode, submitted.Where(item => item.ParticipantId == row.ParticipantId)
                .Select(item => new TheoryPaperScore(item.PaperId, item.Attempt, item.Score!.Value, item.SubmittedAt!.Value)).ToArray())
        }).ToArray(), await query.CountAsync(ct));
    }

    public async Task<TheoryReviewPaper?> ReadAsync(Guid actorId, Guid activityId, Guid paperId, DateTimeOffset now, CancellationToken ct)
    {
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanObserve) return null;
        var row = await Rows(Papers(activityId).Where(paper => paper.Id == paperId)).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        if (!access.CanJudge) return new(row, []);
        var paper = await db.TheoryPapers.AsNoTracking().Include(paper => paper.Items).ThenInclude(item => item.Options).SingleAsync(paper => paper.Id == paperId, ct);
        var ids = paper.Items.Select(item => item.RevisionId).ToArray();
        var versions = await db.TheoryQuestionRevisions.AsNoTracking().Where(version => ids.Contains(version.Id)).Include(version => version.Options).ToDictionaryAsync(version => version.Id, ct);
        var state = paper.State;
        paper.State = TheoryPaperState.Active; // Explicit staff projection of the already-issued content only.
        var questions = ReadTheoryPaper.Project(paper, versions).Questions;
        paper.State = state;
        var corrections = await db.Set<TheoryGradingOverride>().AsNoTracking().Include(item => item.Options).Where(item => item.ActivityId == activityId && ids.Contains(item.RevisionId)).ToDictionaryAsync(item => item.RevisionId, ct);
        return new(row, questions.Select(question =>
        {
            var item = paper.Items.Single(item => item.Id == question.Id);
            var version = versions[item.RevisionId];
            var correct = corrections.TryGetValue(item.RevisionId, out var rule)
                ? rule.Options.Select(option => option.OptionId).ToArray() : version.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToArray();
            return new TheoryReviewQuestion(question, item.RevisionId, correct, version.Explanation, item.Result);
        }).ToArray());
    }

    public async Task<TheoryCorrectionResult> PreviewAsync(Guid actorId, Guid activityId, TheoryCorrectionInput input, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        return await PreviewCoreAsync(actorId, activityId, input, now.AddMinutes(5), now, ct);
    }

    private async Task<TheoryCorrectionResult> PreviewCoreAsync(Guid actorId, Guid activityId, TheoryCorrectionInput input, DateTimeOffset expires,
        DateTimeOffset now, CancellationToken ct)
    {
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct, includeCandidates: true);
        if (access is null || !access.CanManage || !access.Activity.Rules.Any(rule => rule.Candidates.Any(candidate => candidate.RevisionId == input.RevisionId)))
            return new(Failure: TheoryReviewFailure.NotFoundOrForbidden);
        var question = await db.TheoryQuestionRevisions.AsNoTracking().Include(question => question.Options).SingleAsync(question => question.Id == input.RevisionId, ct);
        if (!TheoryCorrectionPolicy.IsValid(question, input.Mode, input.CorrectOptions)) return new(Failure: TheoryReviewFailure.InvalidCorrection);
        var allPapers = await Papers(activityId).AsNoTracking().Where(paper => paper.State == TheoryPaperState.Submitted)
            .Include(paper => paper.Items).ThenInclude(item => item.Options).ToArrayAsync(ct);
        var papers = allPapers.Where(paper => paper.Items.Any(item => item.RevisionId == input.RevisionId)).ToArray();
        long Effective(Guid participantId) => TheoryPaperGrading.Aggregate(access.Activity.ScoringMode,
            allPapers.Where(paper => paper.ParticipantId == participantId).Select(paper => new TheoryPaperScore(paper.Id, paper.Attempt,
                TheoryPaperGrading.Points(paper), paper.SubmittedAt!.Value)).ToArray()).Points;
        var beforeScores = papers.Select(paper => paper.ParticipantId).Distinct().ToDictionary(id => id, Effective);
        var names = (await Rows(Papers(activityId)).ToArrayAsync(ct)).GroupBy(row => row.ParticipantId).ToDictionary(group => group.Key, group => group.First().Name);
        var policy = new TheoryGradingOverride { RevisionId = input.RevisionId, Mode = input.Mode,
            Options = input.CorrectOptions.Select(id => new TheoryGradingOption { OptionId = id }).ToList() };
        var impacts = papers.Select(paper =>
        {
            var before = TheoryPaperGrading.Points(paper);
            TheoryCorrectionPolicy.Apply(paper.Items.Single(item => item.RevisionId == input.RevisionId), policy);
            return new TheoryScoreImpact(paper.Id, paper.ParticipantId, before, TheoryPaperGrading.Points(paper));
        }).ToArray();
        var signature = $"{actorId:N}:{activityId:N}:{input.RevisionId:N}:{input.Mode}:{expires.UtcTicks}:{access.Activity.ConcurrencyStamp:N}:"
            + string.Join(',', input.CorrectOptions.Order()) + ":" + string.Join(';', allPapers.OrderBy(paper => paper.Id).Select(paper => $"{paper.Id:N}:{paper.ConcurrencyStamp:N}"));
        var token = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(signature)).AsSpan(0, 16));
        return new(new(token, expires, papers.Length, impacts)
        { Participants = beforeScores.Select(pair => new TheoryParticipantScoreImpact(pair.Key, names[pair.Key], pair.Value, Effective(pair.Key))).ToArray() });
    }

    public async Task<TheoryCorrectionResult> CorrectAsync(Guid actorId, Guid activityId, TheoryCorrectionInput input, Guid token,
        DateTimeOffset expiresAt, string reason, DateTimeOffset now, CancellationToken ct)
    {
        try { return await CorrectCoreAsync(actorId, activityId, input, token, expiresAt, reason, now, ct); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is DbException { SqlState: "40001" or "40P01" or "23505" })
        { db.ChangeTracker.Clear(); return new(Failure: TheoryReviewFailure.Conflict); }
    }

    private async Task<TheoryCorrectionResult> CorrectCoreAsync(Guid actorId, Guid activityId, TheoryCorrectionInput input, Guid token,
        DateTimeOffset expiresAt, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) return new(Failure: TheoryReviewFailure.ReasonRequired);
        if (expiresAt <= now || expiresAt > now.AddMinutes(5)) return new(Failure: TheoryReviewFailure.PreviewChanged);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var preview = await PreviewCoreAsync(actorId, activityId, input, expiresAt, now, ct);
        if (preview.Failure is not null) return preview;
        if (preview.Preview!.Token != token) return new(Failure: TheoryReviewFailure.PreviewChanged);
        var rule = await db.Set<TheoryGradingOverride>().Include(item => item.Options).SingleOrDefaultAsync(item => item.ActivityId == activityId && item.RevisionId == input.RevisionId, ct);
        if (rule is null) { rule = new() { ActivityId = activityId, RevisionId = input.RevisionId }; db.Add(rule); }
        else { db.RemoveRange(rule.Options); rule.Options.Clear(); await db.SaveChangesAsync(ct); }
        rule.TargetRevision = Guid.NewGuid(); rule.Mode = input.Mode;
        rule.Options = input.CorrectOptions.Select(id => new TheoryGradingOption { OptionId = id }).ToList();
        var activity = await db.TheoryActivities.SingleAsync(item => item.Id == activityId, ct);
        db.Entry(activity).Property(item => item.ConcurrencyStamp).IsModified = true;
        db.Add(new TheoryActivityOperation { Id = Guid.NewGuid(), ActivityId = activityId, ActorId = actorId, RevisionId = input.RevisionId,
            Kind = input.Mode == TheoryCorrectionMode.FullCredit ? TheoryActivityOperationKind.FullCreditGranted : TheoryActivityOperationKind.AnswerCorrected,
            OccurredAt = now, Reason = reason });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await publisher.PublishAsync(new ApplyTheoryCorrection(activityId, input.RevisionId, rule.TargetRevision));
        await publisher.FlushCommittedMessagesAsync();
        return new(TargetRevision: rule.TargetRevision);
    }

    public async Task ApplyAsync(ApplyTheoryCorrection message, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var rule = await db.Set<TheoryGradingOverride>().Include(item => item.Options).SingleOrDefaultAsync(item => item.ActivityId == message.ActivityId && item.RevisionId == message.RevisionId, ct);
        if (rule is null || rule.TargetRevision != message.TargetRevision || rule.AppliedRevision == rule.TargetRevision) return;
        var papers = await Papers(message.ActivityId).Where(paper => paper.State == TheoryPaperState.Submitted
            && paper.Items.Any(item => item.RevisionId == message.RevisionId && item.AppliedGradingRevision != message.TargetRevision))
            .OrderBy(paper => paper.Id).Take(100).Include(paper => paper.Items).ThenInclude(item => item.Options).ToArrayAsync(ct);
        foreach (var paper in papers)
        {
            TheoryCorrectionPolicy.Apply(paper.Items.Single(item => item.RevisionId == message.RevisionId), rule);
            paper.ConcurrencyStamp = Guid.NewGuid();
        }
        db.Entry(rule).Property(item => item.ConcurrencyStamp).IsModified = true;
        if (papers.Length < 100) rule.AppliedRevision = rule.TargetRevision;
        await QueueFactsAsync(papers.Select(paper => paper.Id).ToArray(), now, ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await NotifyAsync(message.ActivityId, ct);
        if (papers.Length == 100) { await publisher.PublishAsync(message); await publisher.FlushCommittedMessagesAsync(); }
    }

    public async Task<TheoryReviewFailure?> VoidPaperAsync(Guid actorId, Guid activityId, Guid paperId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) return TheoryReviewFailure.ReasonRequired;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanManage) return TheoryReviewFailure.NotFoundOrForbidden;
        var paper = await Papers(activityId).SingleOrDefaultAsync(paper => paper.Id == paperId, ct);
        if (paper is null) return TheoryReviewFailure.NotFoundOrForbidden;
        if (paper.State == TheoryPaperState.Voided) return null;
        paper.State = TheoryPaperState.Voided;
        var slot = await db.Set<TheoryActivePaper>().SingleOrDefaultAsync(slot => slot.PaperId == paper.Id, ct);
        if (slot is not null) db.Remove(slot);
        db.Add(new TheoryActivityOperation { Id = Guid.NewGuid(), ActivityId = activityId, ActorId = actorId, PaperId = paper.Id,
            Kind = TheoryActivityOperationKind.PaperVoided, OccurredAt = now, Reason = reason });
        await QueueFactsAsync([paper.Id], now, ct); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await NotifyAsync(activityId, ct);
        return null;
    }

    private Task QueueFactsAsync(Guid[] ids, DateTimeOffset now, CancellationToken ct)
    {
        var stamp = Guid.NewGuid();
        return db.GameplayFacts.Where(fact => fact.TheoryPaperId != null && ids.Contains(fact.TheoryPaperId.Value))
            .ExecuteUpdateAsync(setters => setters.SetProperty(fact => fact.State, GameplayFactState.Queued).SetProperty(fact => fact.UpdatedAt, now)
                .SetProperty(fact => fact.ConcurrencyStamp, stamp), ct);
    }
    private async Task NotifyAsync(Guid activityId, CancellationToken ct)
    {
        var competitionId = await db.CompetitionTheoryActivities.Where(link => link.ActivityId == activityId).Select(link => (Guid?)link.CompetitionId).SingleOrDefaultAsync(ct);
        if (competitionId is not null)
        {
            await leaderboard.InvalidateAsync(competitionId.Value, ct);
            await publisher.PublishAsync(new ProjectLeaderboard(competitionId.Value)); await publisher.FlushCommittedMessagesAsync();
        }
        if (signals is not null) await signals.PublishAsync(new(activityId, null), ct);
    }
    private IQueryable<TheoryPaper> Papers(Guid activityId) => db.TheoryPapers.Where(paper => db.TheoryParticipants.Any(participant => participant.Id == paper.ParticipantId && participant.ActivityId == activityId));
    private IQueryable<TheoryReviewRow> Rows(IQueryable<TheoryPaper> papers) => papers.AsNoTracking()
        .Join(db.TheoryParticipants.AsNoTracking(), paper => paper.ParticipantId, participant => participant.Id, (paper, participant) => new TheoryReviewRow(
            paper.Id, participant.Id, participant.UserId, participant.TeamId,
            participant.TeamId != null ? db.Teams.IgnoreQueryFilters().Where(team => team.Id == participant.TeamId).Select(team => team.Name).Single()
                : db.Users.Where(user => user.Id == participant.UserId).Select(user => user.UserName).Single(),
            paper.Attempt, paper.State, paper.OpenedAt, paper.SubmittedAt,
            paper.State == TheoryPaperState.Submitted ? paper.Items.Where(item => item.Result == TheoryAnswerResult.Correct || item.Result == TheoryAnswerResult.FullCredit).Sum(item => (long?)item.Points) ?? 0 : null)
            { QuestionCount = paper.Items.Count, AnsweredCount = paper.Items.Count(item => item.Options.Any(option => option.Selected)), SubmissionKind = paper.SubmissionKind });
}
