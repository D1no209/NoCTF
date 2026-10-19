using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Theory.Activities;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Theory.Realtime;

namespace NoCTF.Infrastructure.Theory.Papers;

public sealed class TheoryPaperStore(NoCtfDbContext db, IPostCommitMessagePublisher? publisher = null, ILeaderboardCache? leaderboard = null,
    ITheoryChangePublisher? signals = null) : ITheoryPaperStore
{
    private readonly HashSet<Guid> changedCompetitions = [];
    private readonly HashSet<TheoryChanged> changes = [];
    public Task<TheoryPaperResult> ReadAsync(TheoryPaperCommand command, CancellationToken ct)
        => ExecuteAsync(command, PaperAction.Read, null, ct);
    public Task<TheoryPaperResult> IssueAsync(TheoryPaperCommand command, CancellationToken ct)
        => ExecuteAsync(command, PaperAction.Issue, null, ct);
    public Task<TheoryPaperResult> SubmitAsync(TheoryPaperCommand command, CancellationToken ct)
        => ExecuteAsync(command, PaperAction.Submit, null, ct);
    public Task<TheoryPaperResult> AnswerAsync(TheoryAnswerCommand command, CancellationToken ct)
        => ExecuteAsync(new(command.ActorId, command.ActivityId, command.PaperId, null, command.Now), PaperAction.Answer, command, ct);

    private async Task<TheoryPaperResult> ExecuteAsync(TheoryPaperCommand command, PaperAction action, TheoryAnswerCommand? answer, CancellationToken ct)
    {
        TheoryPaperResult? result = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            changedCompetitions.Clear();
            changes.Clear();
            try { result = await ExecuteOnceAsync(command, action, answer, ct); break; }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException
                || exception.GetBaseException() is DbException { SqlState: "40001" or "40P01" or "23505" })
            {
                db.ChangeTracker.Clear();
                if (attempt == 5) return new(Failure: action == PaperAction.Answer ? TheoryPaperFailure.AnswerConflict : TheoryPaperFailure.PaperConflict);
                await Task.Delay(RandomNumberGenerator.GetInt32(5, 25) * (attempt + 1), ct);
            }
        }
        if (result is null) throw new InvalidOperationException("Theory transaction retries exhausted.");
        if (result.Failure is null) await NotifyCommittedAsync(ct);
        return result;
    }

    private async Task<TheoryPaperResult> ExecuteOnceAsync(TheoryPaperCommand command, PaperAction action, TheoryAnswerCommand? answer, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await TheoryActivityAccess.ReadAsync(db, command.ActivityId, command.ActorId, command.Now, ct, includeCandidates: action == PaperAction.Issue);
        if (access is null || !access.CanParticipate || access.Activity.State == TheoryActivityState.Draft)
            return new(Failure: TheoryPaperFailure.NotFoundOrForbidden);
        var activity = access.Activity;
        if (action == PaperAction.Issue && (command.RequestId is null || command.RequestId == Guid.Empty))
            return new(Failure: TheoryPaperFailure.InvalidRequestIdentity);
        if (activity.State == TheoryActivityState.Voided) return new(Failure: TheoryPaperFailure.Voided);
        var participant = await db.TheoryParticipants.SingleOrDefaultAsync(item => item.ActivityId == activity.Id
            && (access.TeamId != null ? item.TeamId == access.TeamId : item.UserId == command.ActorId && item.Kind == TheoryParticipantKind.Individual), ct);
        TheoryPaper? paper = null;
        if (participant is not null)
        {
            paper = command.PaperId is { } paperId
                ? await Papers().SingleOrDefaultAsync(item => item.Id == paperId && item.ParticipantId == participant.Id, ct)
                : await Papers().Where(item => item.ParticipantId == participant.Id).OrderByDescending(item => item.Attempt).FirstOrDefaultAsync(ct);
            if (action == PaperAction.Issue && await db.Set<TheoryIssueReceipt>().AsNoTracking().SingleOrDefaultAsync(item => item.ParticipantId == participant.Id
                    && item.RequestId == command.RequestId, ct) is { } issueReceipt)
            {
                var original = await Papers().SingleAsync(item => item.Id == issueReceipt.PaperId, ct);
                var originalDeadline = TheoryClock.Paper(original.OpenedAt, activity.DurationSeconds, access.Deadline, access.Pauses, command.Now);
                await ExpireAsync(original, activity.Id, originalDeadline, command.Now, activity.State == TheoryActivityState.Finished, ct);
                await EnsureFactAsync(access, participant, original, command.Now, ct);
                await db.SaveChangesAsync(ct);
                var originalView = await ViewAsync(access, participant, original, originalDeadline, command.Now, ct);
                await transaction.CommitAsync(ct);
                return new(originalView);
            }
        }
        if (command.PaperId is not null && paper is null) return new(Failure: TheoryPaperFailure.NotFoundOrForbidden);
        var deadline = paper is null ? access.Deadline : TheoryClock.Paper(paper.OpenedAt, activity.DurationSeconds, access.Deadline, access.Pauses, command.Now);
        if (paper is { State: TheoryPaperState.Active } && command.Now >= deadline.At)
        {
            await ExpireAsync(paper, activity.Id, deadline, command.Now, activity.State == TheoryActivityState.Finished || access.Competition?.Status == CompetitionStatus.Finished, ct);
            await db.SaveChangesAsync(ct);
        }
        if (action == PaperAction.Issue && paper?.State != TheoryPaperState.Active)
        {
            if (activity.State != TheoryActivityState.Published || command.Now >= access.Deadline.At) return new(Failure: TheoryPaperFailure.DeadlineReached);
            if (command.Now < activity.OpensAt || access.Competition is { Status: not CompetitionStatus.Running and not CompetitionStatus.Paused }) return new(Failure: TheoryPaperFailure.NotOpen);
            if (access.Deadline.Paused || access.Competition?.Status == CompetitionStatus.Paused) return new(Failure: TheoryPaperFailure.Paused);
            if (activity.IssuanceStopped) return new(Failure: TheoryPaperFailure.IssuanceStopped);
            participant ??= new() { Id = Guid.NewGuid(), ActivityId = activity.Id,
                Kind = access.TeamId is null ? TheoryParticipantKind.Individual : TheoryParticipantKind.Team,
                UserId = access.TeamId is null ? command.ActorId : null, TeamId = access.TeamId };
            if (activity.AttemptLimit is { } limit && participant.IssuedCount >= limit) return new(Failure: TheoryPaperFailure.AttemptsExhausted);
            if (participant.IssuedCount != command.ExpectedIssuedCount) return new(Failure: TheoryPaperFailure.PaperConflict);
            var used = await db.Set<TheoryDrawReceipt>().Where(receipt => receipt.ParticipantId == participant.Id).Select(receipt => receipt.SourceId).ToArrayAsync(ct);
            var allocation = TheoryPaperAllocator.Allocate(activity.Rules, used.ToHashSet(),
                activity.AttemptLimit is { } attempts ? attempts - participant.IssuedCount : 1, ct);
            if (!allocation.Succeeded) return new(Failure: TheoryPaperFailure.InsufficientQuestions, Shortages: allocation.Shortages);
            var assigned = allocation.Questions.Where(item => item.Attempt == 0).ToArray();
            var sources = assigned.Select(item => item.SourceId).ToArray();
            // A previously published practice cannot expose material subsequently reserved for a formal competition.
            if (activity.Kind != TheoryActivityKind.Competition
                && (await TheoryReservations.ReadAsync(db, sources, activity.Id, command.Now, ct)).Count > 0)
                return new(Failure: TheoryPaperFailure.IssuanceStopped);
            if (db.Entry(participant).State == EntityState.Detached) db.TheoryParticipants.Add(participant);
            participant.IssuedCount++;
            paper = new() { Id = Guid.CreateVersion7(command.Now), ParticipantId = participant.Id, Attempt = participant.IssuedCount,
                OpenedAt = command.Now };
            var versions = await VersionsAsync(assigned.Select(item => item.RevisionId).ToArray(), ct);
            foreach (var assignedQuestion in assigned)
            {
                var options = versions[assignedQuestion.RevisionId].Options.Select(option => option.Id).ToArray();
                TheoryPaperAllocator.Shuffle(options);
                paper.Items.Add(new() { Id = Guid.NewGuid(), RevisionId = assignedQuestion.RevisionId, RuleId = assignedQuestion.RuleId,
                    Order = paper.Items.Count, Points = assignedQuestion.Points,
                    Options = options.Select((id, order) => new TheoryPaperOption { OptionId = id, Order = order }).ToList() });
                db.Add(new TheoryDrawReceipt { ParticipantId = participant.Id, PaperId = paper.Id, SourceId = assignedQuestion.SourceId, IssuedAt = command.Now });
            }
            db.TheoryPapers.Add(paper);
            db.Add(new TheoryActivePaper { ParticipantId = participant.Id, PaperId = paper.Id });
            var exposed = await db.Set<TheoryQuestionExposure>().Where(item => item.ActivityId == activity.Id && sources.Contains(item.SourceId)).Select(item => item.SourceId).ToArrayAsync(ct);
            var sourceRows = await db.Set<TheoryQuestionSource>().Where(source => sources.Contains(source.Id)).ToArrayAsync(ct);
            foreach (var source in sourceRows)
                if (source.FirstExposedAt is null || source.FirstExposedAt > command.Now) source.FirstExposedAt = command.Now;
            foreach (var source in sources.Except(exposed)) db.Add(new TheoryQuestionExposure { SourceId = source, ActivityId = activity.Id, FirstExposedAt = command.Now });
            await db.SaveChangesAsync(ct);
            deadline = TheoryClock.Paper(paper.OpenedAt, activity.DurationSeconds, access.Deadline, access.Pauses, command.Now);
            changes.Add(new(activity.Id, participant.Id));
        }
        else if (action == PaperAction.Answer)
        {
            if (paper is null || answer is null) return new(Failure: TheoryPaperFailure.NotFoundOrForbidden);
            if (answer.RequestId == Guid.Empty) return new(Failure: TheoryPaperFailure.InvalidRequestIdentity);
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", answer.OptionIds.Order().Select(id => id.ToString("N")))));
            var receipt = await db.Set<TheoryAnswerReceipt>().SingleOrDefaultAsync(item => item.ItemId == answer.ItemId && item.RequestId == answer.RequestId, ct);
            if (receipt is not null)
            {
                if (receipt.ActorId != command.ActorId || !CryptographicOperations.FixedTimeEquals(receipt.SelectionDigest, digest))
                    return new(Failure: TheoryPaperFailure.InvalidRequestIdentity);
            }
            else
            {
                var item = paper.Items.SingleOrDefault(item => item.Id == answer.ItemId);
                if (item is null) return new(Failure: TheoryPaperFailure.NotFoundOrForbidden);
                var question = (await VersionsAsync([item.RevisionId], ct))[item.RevisionId];
                var failure = TheoryPaperWorkflow.SaveAnswer(paper, question, deadline with { Paused = deadline.Paused || access.Competition?.Status == CompetitionStatus.Paused },
                    new(answer.ItemId, answer.ExpectedRevision, answer.OptionIds, command.ActorId, command.Now));
                if (failure is not null) return new(Failure: failure);
                await db.SaveChangesAsync(ct);
                db.Add(new TheoryAnswerReceipt { ItemId = item.Id, RequestId = answer.RequestId, ActorId = command.ActorId,
                    ResultingRevision = item.ConcurrencyStamp, SavedAt = command.Now, SelectionDigest = digest });
                await db.SaveChangesAsync(ct);
                changes.Add(new(activity.Id, paper.ParticipantId));
            }
        }
        else if (action == PaperAction.Submit)
        {
            if (paper is null) return new(Failure: TheoryPaperFailure.NotFoundOrForbidden);
            var wasActive = paper.State == TheoryPaperState.Active;
            var failure = TheoryPaperWorkflow.Submit(paper, await VersionsAsync(paper.Items.Select(item => item.RevisionId).ToArray(), ct),
                deadline with { Paused = deadline.Paused || access.Competition?.Status == CompetitionStatus.Paused },
                new(command.ExpectedRevision ?? Guid.Empty, command.ActorId, command.Now));
            if (failure is not null) return new(Failure: failure);
            var active = await db.Set<TheoryActivePaper>().SingleOrDefaultAsync(slot => slot.PaperId == paper.Id, ct);
            if (active is not null) db.Remove(active);
            await db.SaveChangesAsync(ct);
            if (wasActive) changes.Add(new(activity.Id, paper.ParticipantId));
        }
        if (action == PaperAction.Issue && paper is not null && participant is not null)
        {
            db.Add(new TheoryIssueReceipt { ParticipantId = participant.Id, PaperId = paper.Id, RequestId = command.RequestId!.Value });
            await db.SaveChangesAsync(ct);
        }
        if (paper is not null && participant is not null)
        {
            await EnsureFactAsync(access, participant, paper, command.Now, ct);
            await db.SaveChangesAsync(ct);
        }
        var view = await ViewAsync(access, participant, paper, deadline, command.Now, ct);
        await transaction.CommitAsync(ct);
        return new(view);
    }

    private async Task<TheoryPaperView> ViewAsync(TheoryAccess access, TheoryParticipant? participant, TheoryPaper? paper, TheoryDeadline deadline, DateTimeOffset now, CancellationToken ct)
    {
        var history = participant is null ? [] : await db.TheoryPapers.AsNoTracking()
            .Where(paper => paper.ParticipantId == participant.Id && paper.State != TheoryPaperState.Active).OrderBy(paper => paper.Attempt)
            .Select(paper => new TheoryPaperHistory(paper.Id, paper.Attempt, paper.State,
                paper.State == TheoryPaperState.Submitted ? paper.Items.Where(item => item.Result == TheoryAnswerResult.Correct || item.Result == TheoryAnswerResult.FullCredit).Sum(item => (long?)item.Points) ?? 0 : null,
                paper.SubmittedAt)).ToArrayAsync(ct);
        var actorIds = paper?.State == TheoryPaperState.Active
            ? paper.Items.Where(item => item.AnsweredById.HasValue).Select(item => item.AnsweredById!.Value).Distinct().ToArray() : [];
        var actors = actorIds.Length == 0 ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking().Where(user => actorIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        return new(TheoryActivityMapping.Summary(access.Activity, access.Competition?.Id), deadline,
            paper is null ? null : ReadTheoryPaper.Project(paper, paper.State == TheoryPaperState.Active
                ? await VersionsAsync(paper.Items.Select(item => item.RevisionId).ToArray(), ct) : new Dictionary<Guid, TheoryQuestionRevision>(), actors),
            participant?.IssuedCount ?? 0, TheoryPaperGrading.Aggregate(access.Activity.ScoringMode, history.Where(item => item.State == TheoryPaperState.Submitted)
                .Select(item => new TheoryPaperScore(item.PaperId, item.Attempt, item.Score!.Value, item.SubmittedAt!.Value)).ToArray()), history)
        {
            ServerTime = now,
            CanAnswer = paper?.State == TheoryPaperState.Active && now < deadline.At && !deadline.Paused
                && access.Activity.State == TheoryActivityState.Published && (access.Competition is null || access.Competition.Status == CompetitionStatus.Running),
            CanIssue = paper?.State != TheoryPaperState.Active && access.Activity.State == TheoryActivityState.Published
                && !access.Activity.IssuanceStopped && now >= access.Activity.OpensAt && now < access.Deadline.At && !access.Deadline.Paused
                && (access.Competition is null || access.Competition.Status == CompetitionStatus.Running)
                && (access.Activity.AttemptLimit is null || (participant?.IssuedCount ?? 0) < access.Activity.AttemptLimit)
        };
    }

    public async Task SweepAsync(Guid activityId, DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.TheoryPapers.AsNoTracking().Where(paper => paper.State == TheoryPaperState.Active
            && db.TheoryParticipants.Any(participant => participant.Id == paper.ParticipantId && participant.ActivityId == activityId))
            .Select(paper => paper.Id).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            for (var retry = 0; retry < 3; retry++)
            {
                changedCompetitions.Clear(); changes.Clear();
                try
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                    var access = await TheoryActivityAccess.ReadAsync(db, activityId, null, now, ct);
                    if (access is null) break;
                    var paper = await Papers().SingleAsync(item => item.Id == id, ct);
                    var deadline = TheoryClock.Paper(paper.OpenedAt, access.Activity.DurationSeconds, access.Deadline, access.Pauses, now);
                    await ExpireAsync(paper, activityId, deadline, now, access.Activity.FinishedAt is not null || access.Competition?.Status == CompetitionStatus.Finished, ct);
                    var participant = await db.TheoryParticipants.SingleAsync(item => item.Id == paper.ParticipantId, ct);
                    await EnsureFactAsync(access, participant, paper, now, ct);
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    break;
                }
                catch (Exception exception) when (retry < 2 && TransactionFailureClassifier.IsRetryable(exception)) { }
                finally { db.ChangeTracker.Clear(); }
            }
            await NotifyCommittedAsync(ct);
        }
    }

    private async Task EnsureFactAsync(TheoryAccess access, TheoryParticipant participant, TheoryPaper paper, DateTimeOffset now, CancellationToken ct)
    {
        if (paper.State != TheoryPaperState.Submitted) return;
        var ids = paper.Items.Select(item => item.RevisionId).ToArray();
        var overrides = await db.Set<TheoryGradingOverride>().AsNoTracking().Include(item => item.Options)
            .Where(item => item.ActivityId == access.Activity.Id && ids.Contains(item.RevisionId)).ToDictionaryAsync(item => item.RevisionId, ct);
        var changed = false;
        foreach (var item in paper.Items)
            if (overrides.TryGetValue(item.RevisionId, out var correction) && item.AppliedGradingRevision != correction.TargetRevision)
            { TheoryCorrectionPolicy.Apply(item, correction); changed = true; }
        if (changed) { paper.ConcurrencyStamp = Guid.NewGuid(); changes.Add(new(access.Activity.Id, participant.Id)); }
        if (access.Competition is null || participant.TeamId is null) return;
        var existing = await db.GameplayFacts.SingleOrDefaultAsync(fact => fact.TheoryPaperId == paper.Id, ct);
        if (existing is not null)
        {
            if (changed)
            {
                existing.State = GameplayFactState.Completed; existing.UpdatedAt = now;
                changedCompetitions.Add(access.Competition.Id);
            }
            return;
        }
        db.GameplayFacts.Add(new TheoryPaperSubmissionGameplayFact
        {
            Id = Guid.CreateVersion7(now), CompetitionId = access.Competition.Id, TheoryPaperId = paper.Id,
            TeamId = participant.TeamId, ActorUserId = paper.SubmittedById, OccurredAt = paper.SubmittedAt!.Value,
            UpdatedAt = now, State = GameplayFactState.Completed, Result = GameplayFactResult.Applied
        });
        changedCompetitions.Add(access.Competition.Id);
    }

    private async Task NotifyCommittedAsync(CancellationToken ct)
    {
        foreach (var id in changedCompetitions)
        {
            if (leaderboard is not null) await leaderboard.InvalidateAsync(id, ct);
            if (publisher is not null) await publisher.PublishAsync(new ProjectLeaderboard(id));
        }
        if (publisher is not null && changedCompetitions.Count != 0) await publisher.FlushCommittedMessagesAsync();
        if (signals is not null) foreach (var change in changes) await signals.PublishAsync(change, ct);
        changedCompetitions.Clear();
        changes.Clear();
    }

    private async Task ExpireAsync(TheoryPaper paper, Guid activityId, TheoryDeadline deadline, DateTimeOffset now, bool finished, CancellationToken ct)
    {
        if (!TheoryPaperWorkflow.Expire(paper, await VersionsAsync(paper.Items.Select(item => item.RevisionId).ToArray(), ct), deadline, now, finished)) return;
        changes.Add(new(activityId, paper.ParticipantId));
        var active = await db.Set<TheoryActivePaper>().SingleOrDefaultAsync(slot => slot.PaperId == paper.Id, ct);
        if (active is not null) db.Remove(active);
    }
    private IQueryable<TheoryPaper> Papers() => db.TheoryPapers.Include(paper => paper.Items).ThenInclude(item => item.Options);
    private Task<Dictionary<Guid, TheoryQuestionRevision>> VersionsAsync(Guid[] ids, CancellationToken ct) => db.TheoryQuestionRevisions.AsNoTracking()
        .Where(item => ids.Contains(item.Id)).Include(item => item.Options).ToDictionaryAsync(item => item.Id, ct);
    private enum PaperAction { Read, Issue, Answer, Submit }
}
