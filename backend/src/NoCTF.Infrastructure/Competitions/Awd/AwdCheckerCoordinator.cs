using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using NoCTF.GameModes.Awd.Checking;
using NoCTF.GameModes.Awd.Flags;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

/// <summary>Reconstructs checks from immutable round rules and recovers publication from pending facts.</summary>
public sealed class AwdCheckerCoordinator(NoCtfDbContext db, IPostCommitMessagePublisher outbox,
    TimeProvider clock, ICompetitionEventRecorder events) : IAwdCheckerCoordinator
{
    public async Task<MessageExecutionOutcome> DispatchAsync(DispatchAwdCheckers message, CancellationToken ct)
    {
        for (var retry = 0; ; retry++)
        {
            try { return await DispatchCoreAsync(message, ct); }
            catch (Exception exception) when (retry < 2 && TransactionFailureClassifier.IsRetryable(exception))
            {
                outbox.DiscardPendingMessages();
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<MessageExecutionOutcome> DispatchCoreAsync(DispatchAwdCheckers message, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        // All samples are persisted together. A crash cannot leave an apparently planned round with missing samples.
        var unplanned = await db.AwdRoundRules.AsSplitQuery().Where(rules =>
                db.Competitions.Any(competition => competition.Id == rules.CompetitionId && competition.Status != CompetitionStatus.Paused)
                && db.AwdRounds.Any(round => round.Id == rules.RoundId && round.EndsAt > rules.EffectiveAt)
                && rules.Teams.Any(team =>
                !db.AwdRoundRules.Any(other => other.RoundId == rules.RoundId
                    && other.CompetitionChallengeId == rules.CompetitionChallengeId && other.Revision < rules.Revision
                    && other.Teams.Any(previousTeam => previousTeam.TeamId == team.TeamId)))
                && !db.GameplayFacts.Any(fact => fact.Kind == GameplayFactKind.AwdServiceTransition
                    && fact.Awd != null && fact.Awd.RulesId == rules.Id))
            .OrderBy(rules => rules.EffectiveAt).ThenBy(rules => rules.Id).Take(100).ToListAsync(ct);
        var added = false;
        foreach (var rules in unplanned)
        {
            var round = await db.AwdRounds.SingleAsync(item => item.Id == rules.RoundId, ct);
            var competition = await db.Competitions.SingleAsync(item => item.Id == rules.CompetitionId, ct);
            var timeline = await AwdRunningClock.ReadAsync(db, competition.Id, competition.StartAt, now, ct);
            var roundStart = timeline.ElapsedAt(round.StartsAt).Ticks;
            var enabled = round.ActiveStartTicks + timeline.ElapsedAt(rules.EffectiveAt).Ticks - roundStart;
            var earlierTeams = await db.AwdRoundRules.Where(other => other.RoundId == round.Id
                    && other.CompetitionChallengeId == rules.CompetitionChallengeId && other.Revision < rules.Revision)
                .SelectMany(other => other.Teams.Select(team => team.TeamId)).Distinct().ToArrayAsync(ct);
            var samples = AwdCheckPlan.Create(round, rules, enabled);
            foreach (var team in rules.Teams.Where(team => !earlierTeams.Contains(team.TeamId)))
            foreach (var sample in samples)
            {
                var scheduled = timeline.At(TimeSpan.FromTicks(roundStart + sample.ActiveTicks - round.ActiveStartTicks), true, clampClosed: true);
                var deadline = timeline.At(TimeSpan.FromTicks(roundStart + sample.DeadlineActiveTicks - round.ActiveStartTicks), false, clampClosed: true);
                var fact = new AwdServiceTransitionGameplayFact
                {
                    Id = AwdCheckPlan.FactId(round.Id, rules.CompetitionChallengeId, team.TeamId, sample.ActiveTicks),
                    CompetitionId = rules.CompetitionId, CompetitionChallengeId = rules.CompetitionChallengeId,
                    TeamId = team.TeamId, ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = round.Id,
                    OccurredAt = scheduled, UpdatedAt = now, State = GameplayFactState.Pending,
                    Awd = new() { RulesId = rules.Id, ReceivedRoundId = round.Id, ReceivedRoundNumber = round.Number,
                        ScheduledAt = scheduled, Deadline = deadline, ReceiptDeadline = deadline.AddSeconds(rules.Options.CallbackGraceSeconds),
                        SampleActiveTicks = sample.ActiveTicks, SampleDeadlineActiveTicks = sample.DeadlineActiveTicks,
                        FinalScheduledCheck = sample.Final, Execution = AwdCheckExecution.Pending }
                };
                db.GameplayFacts.Add(fact);
                added = true;
            }
        }
        await db.SaveChangesAsync(ct);
        var pending = await db.GameplayFacts.Include(fact => fact.Awd).ThenInclude(details => details!.Flags)
            .Where(fact => fact.Kind == GameplayFactKind.AwdServiceTransition && fact.Awd != null
                && fact.Awd.Settlement == AwdSettlementState.Provisional
                && fact.Awd.Execution != AwdCheckExecution.Completed
                && (message.AfterGameplayFactId == null || fact.Id.CompareTo(message.AfterGameplayFactId.Value) > 0))
            .OrderBy(fact => fact.Id).Take(500).AsSplitQuery().ToListAsync(ct);
        var applied = added;
        foreach (var fact in pending)
        {
            var details = fact.Awd!;
            var rules = await db.AwdRoundRules.SingleAsync(item => item.Id == details.RulesId, ct);
            var competition = await db.Competitions.SingleAsync(item => item.Id == fact.CompetitionId, ct);
            var round = await db.AwdRounds.SingleAsync(item => item.Id == details.ReceivedRoundId, ct);
            if (competition.Status == CompetitionStatus.Paused && details.Execution == AwdCheckExecution.Pending) continue;
            if (details.Execution == AwdCheckExecution.Pending && competition.Status == CompetitionStatus.Running)
            {
                var timeline = await AwdRunningClock.ReadAsync(db, competition.Id, competition.StartAt, now, ct);
                var start = timeline.ElapsedAt(round.StartsAt).Ticks;
                details.ScheduledAt = timeline.At(TimeSpan.FromTicks(start + details.SampleActiveTicks!.Value - round.ActiveStartTicks), true);
                details.Deadline = timeline.At(TimeSpan.FromTicks(start + details.SampleDeadlineActiveTicks!.Value - round.ActiveStartTicks), false);
                details.ReceiptDeadline = details.Deadline.Value.AddSeconds(rules.Options.CallbackGraceSeconds);
                fact.OccurredAt = details.ScheduledAt.Value;
            }
            if (details.AttemptStartedAt is null && competition.Status == CompetitionStatus.Finished)
            {
                await HoldAsync(fact, AwdCheckReason.TaskNotExecuted, now, ct);
                applied = true;
                continue;
            }
            if (now < details.ScheduledAt) continue;
            if (now > details.ReceiptDeadline || details.AttemptStartedAt is null && now >= details.Deadline)
            {
                await HoldAsync(fact, details.AttemptStartedAt is null ? AwdCheckReason.TaskNotExecuted : AwdCheckReason.CallbackMissing, now, ct);
                applied = true;
                continue;
            }
            if (competition.Status != CompetitionStatus.Running || now >= round.EndsAt)
                continue;
            var competitionDeadline = await AwdCompetitionDeadline.ReadAsync(db, competition.Id, competition.StartAt,
                competition.EndAt, (AwdCompetitionModeConfiguration)competition.ModeConfiguration!, now, ct);
            if (now >= competitionDeadline.EndsAt) continue;
            if (AwdCheckPlan.CanRetry(details, rules.Options, now))
            {
                details.Execution = AwdCheckExecution.Pending;
                details.AttemptId = null;
                details.AttemptStartedAt = null;
                details.AttemptDeadline = null;
                details.ReceivedAt = null;
                details.EvidenceComplete = false;
            }
            if (details.Execution != AwdCheckExecution.Pending || now >= details.Deadline) continue;
            var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(instance => instance.CompetitionChallengeId == fact.CompetitionChallengeId
                && instance.TeamId == fact.TeamId && instance.State == RuntimeState.Running
                && instance.Purpose == RuntimePurpose.Player && instance.RunnerId != null && instance.ProviderReceipt != null, ct);
            if (runtime is null) continue;
            if (details.AttemptId is null)
            {
                details.Attempt++;
                details.AttemptId = Guid.NewGuid();
                details.RuntimeId = runtime.Id;
                // The manifest is fixed before dispatch. A retry reuses precisely these flag identities.
                if (details.Attempt == 1)
                    await PopulateManifestAsync(fact, rules, round, now, ct);
                details.Health = null;
                details.Reason = AwdCheckReason.None;
                fact.Result = null;
                fact.State = GameplayFactState.Queued;
                fact.UpdatedAt = now;
                fact.ConcurrencyStamp = Guid.NewGuid();
                applied = true;
            }
            if (details.RuntimeId != runtime.Id)
            {
                await HoldAsync(fact, AwdCheckReason.RuntimeSuperseded, now, ct);
                applied = true;
                continue;
            }
            // Re-publish pending attempts on every scan; the Runner fences duplicate execution.
            await outbox.PublishToRunnerNodeAsync(new RunAwdChecker(runtime.Id, fact.CompetitionChallengeId,
                fact.Id, runtime.RunnerId!, details.Deadline!.Value, details.AttemptId.Value, details.ReceiptDeadline!.Value));
            await outbox.ScheduleAsync(new AwdCheckerCallbackMissing(fact.CompetitionId, fact.CompetitionChallengeId,
                runtime.Id, fact.Id, details.ReceiptDeadline.Value, details.AttemptId.Value), details.ReceiptDeadline.Value.AddMilliseconds(1));
        }
        if (pending.Count == 500)
            await outbox.PublishAsync(new DispatchAwdCheckers(now, pending[^1].Id));
        if (unplanned.Count == 100 && message.AfterGameplayFactId is null)
            await outbox.PublishAsync(new DispatchAwdCheckers(now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return applied ? MessageExecutionOutcome.Applied : MessageExecutionOutcome.Idempotent;
    }

    private async Task PopulateManifestAsync(GameplayFact fact, AwdRoundRules rules, AwdRound round,
        DateTimeOffset now, CancellationToken ct)
    {
        var details = fact.Awd!;
        var issues = await db.AwdFlagIssues.Where(issue => issue.CompetitionChallengeId == fact.CompetitionChallengeId
            && issue.TeamId == fact.TeamId && issue.State == AwdFlagIssueState.Issued && issue.IssuedAt <= now).ToListAsync(ct);
        var competition = await db.Competitions.SingleAsync(item => item.Id == fact.CompetitionId, ct);
        var timeline = await AwdRunningClock.ReadAsync(db, competition.Id, competition.StartAt, now, ct);
        var activeTicks = round.ActiveStartTicks + timeline.ElapsedAt(now).Ticks - timeline.ElapsedAt(round.StartsAt).Ticks;
        var generationDue = AwdGenerationPlan.IsDue(rules, round.Number);
        var slot = AwdGenerationPlan.Slot(rules, round.Number, activeTicks);
        var epochRuleIds = await db.AwdRoundRules.Where(snapshot => snapshot.CompetitionChallengeId == fact.CompetitionChallengeId
                && snapshot.GenerationEpochId == rules.GenerationEpochId)
            .Select(snapshot => snapshot.Id).ToArrayAsync(ct);
        details.ManifestComplete = !generationDue || rules.Stores.All(store =>
            issues.Count(issue => issue.StoreId == store.StoreId && epochRuleIds.Contains(issue.RulesId)
                && issue.GenerationSlot == slot) >= store.FlagsPerIssue);
        foreach (var issue in issues.Where(issue => AwdFlagLifetime.RequiresRead(issue, round.Number, activeTicks)))
        {
            var issueRules = issue.RulesId == rules.Id ? rules
                : await db.AwdRoundRules.SingleAsync(item => item.Id == issue.RulesId, ct);
            var store = issueRules.Stores.Single(item => item.StoreId == issue.StoreId);
            details.Flags.Add(new()
            {
                FlagId = issue.FlagId, Historical = issue.RoundId != round.Id, Obligation = issue.Obligation,
                RequiresWrite = issue.RoundId == round.Id && store.DeliveryMode == AwdFlagDeliveryMode.BusinessChecker
                    && issue.DeliveryState != AwdFlagDeliveryState.Succeeded
            });
        }
    }

    public async Task ExpireAsync(AwdCheckerCallbackMissing message, CancellationToken ct)
    {
        // Expiry and periodic recovery use the same relational state. Stale timeout messages cannot overwrite a result.
        await DispatchAsync(new(clock.GetUtcNow()), ct);
    }

    private async Task HoldAsync(GameplayFact fact, AwdCheckReason reason, DateTimeOffset now, CancellationToken ct)
    {
        var details = fact.Awd!;
        details.Execution = AwdCheckExecution.FailedToDetermine;
        details.Settlement = AwdSettlementState.PendingAdjudication;
        details.Health = AwdHealth.InternalError;
        details.Reason = reason;
        fact.State = GameplayFactState.PlatformFailed;
        fact.Result = null;
        fact.FailureCode = GameplayFactFailureCode.CheckerPlatformError;
        fact.UpdatedAt = now;
        fact.ConcurrencyStamp = Guid.NewGuid();
        await AwdAdjudicationNotification.AddAsync(db, fact, now, ct);
        await events.RecordAsync(new(fact.CompetitionId, CompetitionEventKind.ScoringRecorded,
            CompetitionEventLevel.Warning, CompetitionEventVisibility.Team, now, TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId, RuntimeInstanceId: details.RuntimeId,
            GameplayFactId: fact.Id, GameplayFactKind: fact.Kind, GameplayFactState: fact.State), ct);
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
    }
}
