using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Checking;
using NoCTF.GameModes.Awd.Flags;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

/// <summary>Reconstructs checks from immutable round rules and recovers publication from pending facts.</summary>
public sealed class AwdCheckerCoordinator(NoCtfDbContext db, IPostCommitMessagePublisher outbox,
    TimeProvider clock, ICompetitionEventRecorder events) : IAwdCheckerCoordinator
{
    public Task<MessageExecutionOutcome> DispatchAsync(DispatchAwdCheckers message, CancellationToken ct) =>
        DispatchAsync(message, null, ct);

    private async Task<MessageExecutionOutcome> DispatchAsync(DispatchAwdCheckers message,
        AwdCheckerCallbackMissing? expiry, CancellationToken ct)
    {
        for (var retry = 0; ; retry++)
        {
            try { return await DispatchCoreAsync(message, expiry, ct); }
            catch (Exception exception) when (retry < 2 && TransactionFailureClassifier.IsRetryable(exception))
            {
                outbox.DiscardPendingMessages();
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<MessageExecutionOutcome> DispatchCoreAsync(DispatchAwdCheckers message,
        AwdCheckerCallbackMissing? expiry, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var batch = new DispatchBatch(db, now);
        // All samples are persisted together. A crash cannot leave an apparently planned round with missing samples.
        var unplanned = expiry is not null ? [] : await db.AwdRoundRules.AsSplitQuery().Where(rules =>
                db.Competitions.Any(competition => competition.Id == rules.CompetitionId && competition.Status != CompetitionStatus.Paused)
                && db.AwdRounds.Any(round => round.Id == rules.RoundId && round.EndsAt > rules.EffectiveAt)
                && rules.Teams.Any(team =>
                !db.AwdRoundRules.Any(other => other.RoundId == rules.RoundId
                    && other.CompetitionChallengeId == rules.CompetitionChallengeId && other.Revision < rules.Revision
                    && other.Teams.Any(previousTeam => previousTeam.TeamId == team.TeamId)))
                && !db.GameplayFacts.Any(fact => fact.Kind == GameplayFactKind.AwdServiceTransition
                    && fact.Awd != null && fact.Awd.RulesId == rules.Id))
            .OrderBy(rules => rules.EffectiveAt).ThenBy(rules => rules.Id).Take(100).ToListAsync(ct);
        foreach (var rules in unplanned) batch.Rules.Add(rules.Id, rules);
        await batch.LoadAsync([], unplanned.Select(rules => rules.CompetitionId),
            unplanned.Select(rules => rules.RoundId), ct);
        var unplannedIds = unplanned.Select(rules => rules.Id).ToArray();
        var previouslyEligible = unplanned.Count == 0 ? [] : await db.AwdRoundRules
            .Where(other => db.AwdRoundRules.Any(rules => unplannedIds.Contains(rules.Id)
                && other.RoundId == rules.RoundId && other.CompetitionChallengeId == rules.CompetitionChallengeId
                && other.Revision < rules.Revision))
            .SelectMany(other => other.Teams.Select(team => new
                { other.RoundId, other.CompetitionChallengeId, other.Revision, team.TeamId })).ToArrayAsync(ct);
        var eligibilityByTarget = previouslyEligible.ToLookup(item => (item.RoundId, item.CompetitionChallengeId));
        var added = false;
        foreach (var rules in unplanned)
        {
            var round = batch.Rounds[rules.RoundId];
            var timeline = await batch.TimelineAsync(rules.CompetitionId, ct);
            var roundStart = timeline.ElapsedAt(round.StartsAt).Ticks;
            var enabled = round.ActiveStartTicks + timeline.ElapsedAt(rules.EffectiveAt).Ticks - roundStart;
            var earlierTeams = eligibilityByTarget[(round.Id, rules.CompetitionChallengeId)]
                .Where(item => item.Revision < rules.Revision).Select(item => item.TeamId).ToHashSet();
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
        var pendingQuery = db.GameplayFacts.Include(fact => fact.Awd).ThenInclude(details => details!.Flags)
            .Where(fact => fact.Kind == GameplayFactKind.AwdServiceTransition && fact.Awd != null
                && fact.Awd.Settlement == AwdSettlementState.Provisional
                && fact.Awd.Execution != AwdCheckExecution.Completed);
        if (expiry is not null)
        {
            // A timeout is only a wakeup for the attempt and receipt window that scheduled it.
            // Compare persisted timestamps in SQL, at the database's timestamp precision.
            pendingQuery = pendingQuery.Where(fact => fact.Id == expiry.GameplayFactId
                && fact.CompetitionId == expiry.CompetitionId && fact.CompetitionChallengeId == expiry.CompetitionChallengeId
                && fact.Awd!.RuntimeId == expiry.RuntimeInstanceId && fact.Awd.AttemptId == expiry.AttemptId
                && fact.Awd.ReceiptDeadline == expiry.OccurredAt && fact.Awd.ReceiptDeadline < now);
        }
        else if (message.AfterGameplayFactId is { } after)
            pendingQuery = pendingQuery.Where(fact => fact.Id.CompareTo(after) > 0);
        var pending = await pendingQuery.OrderBy(fact => fact.Id).Take(500).AsSplitQuery().ToListAsync(ct);
        await batch.LoadAsync(pending.Select(fact => fact.Awd!.RulesId!.Value),
            pending.Select(fact => fact.CompetitionId), pending.Select(fact => fact.Awd!.ReceivedRoundId!.Value), ct);
        var applied = added;
        var ready = new List<GameplayFact>();
        foreach (var fact in pending)
        {
            var details = fact.Awd!;
            var rules = batch.Rules[details.RulesId!.Value];
            var competition = batch.Competitions[fact.CompetitionId];
            var round = batch.Rounds[details.ReceivedRoundId!.Value];
            if (competition.Status == CompetitionStatus.Paused && details.Execution == AwdCheckExecution.Pending) continue;
            if (details.Execution == AwdCheckExecution.Pending && competition.Status == CompetitionStatus.Running)
            {
                var timeline = await batch.TimelineAsync(competition.Id, ct);
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
            var competitionDeadline = await batch.DeadlineAsync(competition.Id, ct);
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
            ready.Add(fact);
        }
        var readyIds = ready.Select(fact => fact.Id).ToArray();
        var runtimes = ready.Count == 0 ? [] : await db.RuntimeInstances.Where(instance => instance.State == RuntimeState.Running
                && instance.Purpose == RuntimePurpose.Player && instance.RunnerId != null && instance.ProviderReceipt != null
                && db.GameplayFacts.Any(fact => readyIds.Contains(fact.Id)
                    && fact.CompetitionChallengeId == instance.CompetitionChallengeId && fact.TeamId == instance.TeamId))
            .Select(instance => new { instance.Id, instance.CompetitionChallengeId, instance.TeamId, instance.RunnerId }).ToArrayAsync(ct);
        var runtimesByTarget = runtimes.ToLookup(instance => (instance.CompetitionChallengeId, instance.TeamId));
        var dispatches = new List<(Guid CompetitionId, RunAwdChecker Message)>();
        var manifests = new List<GameplayFact>();
        foreach (var fact in ready)
        {
            var details = fact.Awd!;
            var runtime = runtimesByTarget[(fact.CompetitionChallengeId, fact.TeamId)].SingleOrDefault();
            if (runtime is null) continue;
            if (details.AttemptId is null)
            {
                details.Attempt++;
                details.AttemptId = Guid.NewGuid();
                details.RuntimeId = runtime.Id;
                // The manifest is fixed before dispatch. A retry reuses precisely these flag identities.
                if (details.Attempt == 1)
                    manifests.Add(fact);
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
            // Recovery is a new transport delivery, even when a failed claim left the execution attempt unchanged.
            // Reusing identical payloads would let JetStream suppress recovery after the earlier message was acknowledged.
            dispatches.Add((fact.CompetitionId, new RunAwdChecker(runtime.Id, fact.CompetitionChallengeId!.Value,
                fact.Id, runtime.RunnerId!, details.Deadline!.Value, details.AttemptId.Value, details.ReceiptDeadline!.Value,
                Guid.NewGuid())));
        }
        await PopulateManifestsAsync(manifests, batch, now, ct);
        foreach (var (competitionId, dispatch) in dispatches)
        {
            await outbox.PublishToRunnerNodeAsync(dispatch);
            await outbox.ScheduleAsync(new AwdCheckerCallbackMissing(competitionId, dispatch.CompetitionChallengeId,
                dispatch.RuntimeInstanceId, dispatch.GameplayFactId, dispatch.ReceiptDeadline, dispatch.AttemptId),
                dispatch.ReceiptDeadline.AddMilliseconds(1));
        }
        if (expiry is null && pending.Count == 500)
            await outbox.PublishAsync(new DispatchAwdCheckers(now, pending[^1].Id));
        if (expiry is null && unplanned.Count == 100 && message.AfterGameplayFactId is null)
            await outbox.PublishAsync(new DispatchAwdCheckers(now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return applied ? MessageExecutionOutcome.Applied : MessageExecutionOutcome.Idempotent;
    }

    private async Task PopulateManifestsAsync(IReadOnlyList<GameplayFact> facts, DispatchBatch batch,
        DateTimeOffset now, CancellationToken ct)
    {
        if (facts.Count == 0) return;
        var factIds = facts.Select(fact => fact.Id).ToArray();
        var issues = await db.AwdFlagIssues.AsNoTracking()
            .Where(issue => issue.State == AwdFlagIssueState.Issued && issue.IssuedAt <= now
                && db.GameplayFacts.Any(fact => factIds.Contains(fact.Id)
                    && fact.CompetitionChallengeId == issue.CompetitionChallengeId && fact.TeamId == issue.TeamId))
            .ToArrayAsync(ct);
        var issuesByTarget = issues.ToLookup(issue => ((Guid?)issue.CompetitionChallengeId, (Guid?)issue.TeamId));
        var rulesIds = facts.Select(fact => fact.Awd!.RulesId!.Value).Distinct().ToArray();
        var epochRules = await db.AwdRoundRules.Where(snapshot => db.AwdRoundRules.Any(rules => rulesIds.Contains(rules.Id)
                && rules.CompetitionChallengeId == snapshot.CompetitionChallengeId && rules.GenerationEpochId == snapshot.GenerationEpochId))
            .Select(snapshot => new { snapshot.Id, snapshot.CompetitionChallengeId, snapshot.GenerationEpochId }).ToArrayAsync(ct);
        var epochRuleIds = epochRules.GroupBy(item => (item.CompetitionChallengeId, item.GenerationEpochId))
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToHashSet());
        var issueRulesIds = issues.Select(issue => issue.RulesId).Distinct().ToArray();
        var stores = await db.AwdRoundRules.Where(rules => issueRulesIds.Contains(rules.Id))
            .SelectMany(rules => rules.Stores.Select(store => new { rules.Id, store.StoreId, store.DeliveryMode })).ToArrayAsync(ct);
        var deliveryModes = stores.ToDictionary(store => (store.Id, store.StoreId), store => store.DeliveryMode);
        foreach (var fact in facts)
        {
            var details = fact.Awd!;
            var rules = batch.Rules[details.RulesId!.Value];
            var round = batch.Rounds[details.ReceivedRoundId!.Value];
            var timeline = await batch.TimelineAsync(fact.CompetitionId, ct);
            var activeTicks = round.ActiveStartTicks + timeline.ElapsedAt(now).Ticks - timeline.ElapsedAt(round.StartsAt).Ticks;
            var generationDue = AwdGenerationPlan.IsDue(rules, round.Number);
            var slot = AwdGenerationPlan.Slot(rules, round.Number, activeTicks);
            var targetIssues = issuesByTarget[(fact.CompetitionChallengeId, fact.TeamId)];
            var generationRules = epochRuleIds[(rules.CompetitionChallengeId, rules.GenerationEpochId)];
            details.ManifestComplete = !generationDue || rules.Stores.All(store =>
                targetIssues.Count(issue => issue.StoreId == store.StoreId && generationRules.Contains(issue.RulesId)
                    && issue.GenerationSlot == slot) >= store.FlagsPerIssue);
            foreach (var issue in targetIssues.Where(issue => AwdFlagLifetime.RequiresRead(issue, round.Number, activeTicks)))
            {
                details.Flags.Add(new()
                {
                    FlagId = issue.FlagId, Historical = issue.RoundId != round.Id, Obligation = issue.Obligation,
                    RequiresWrite = issue.RoundId == round.Id && deliveryModes[(issue.RulesId, issue.StoreId)] == AwdFlagDeliveryMode.BusinessChecker
                        && issue.DeliveryState != AwdFlagDeliveryState.Succeeded
                });
            }
        }
    }

    public async Task ExpireAsync(AwdCheckerCallbackMissing message, CancellationToken ct)
    {
        // Periodic recovery still handles all pending work; a scheduled timeout never starts a global scan.
        await DispatchAsync(new(clock.GetUtcNow()), message, ct);
    }

    // These objects only live inside one serializable attempt. A retry reads a fresh snapshot.
    private sealed class DispatchBatch(NoCtfDbContext db, DateTimeOffset now)
    {
        public Dictionary<Guid, AwdRoundRules> Rules { get; } = [];
        public Dictionary<Guid, Competition> Competitions { get; } = [];
        public Dictionary<Guid, AwdRound> Rounds { get; } = [];
        private readonly Dictionary<Guid, AwdRunningClock> timelines = [];
        private readonly Dictionary<Guid, AwdCompetitionDeadline> deadlines = [];

        public async Task LoadAsync(IEnumerable<Guid> rulesIds, IEnumerable<Guid> competitionIds,
            IEnumerable<Guid> roundIds, CancellationToken ct)
        {
            var missingRules = rulesIds.Distinct().Where(id => !Rules.ContainsKey(id)).ToArray();
            if (missingRules.Length > 0)
                foreach (var rules in await db.AwdRoundRules.AsSplitQuery().Where(rules => missingRules.Contains(rules.Id)).ToListAsync(ct))
                    Rules.Add(rules.Id, rules);
            var missingCompetitions = competitionIds.Distinct().Where(id => !Competitions.ContainsKey(id)).ToArray();
            if (missingCompetitions.Length > 0)
                foreach (var competition in await db.Competitions.AsSplitQuery().Where(competition => missingCompetitions.Contains(competition.Id)).ToListAsync(ct))
                    Competitions.Add(competition.Id, competition);
            var missingRounds = roundIds.Distinct().Where(id => !Rounds.ContainsKey(id)).ToArray();
            if (missingRounds.Length > 0)
                foreach (var round in await db.AwdRounds.AsSplitQuery().Where(round => missingRounds.Contains(round.Id)).ToListAsync(ct))
                    Rounds.Add(round.Id, round);
        }

        public async Task<AwdRunningClock> TimelineAsync(Guid competitionId, CancellationToken ct)
        {
            if (!timelines.TryGetValue(competitionId, out var timeline))
            {
                var competition = Competitions[competitionId];
                timeline = await AwdRunningClock.ReadAsync(db, competitionId, competition.StartAt, now, ct);
                timelines.Add(competitionId, timeline);
            }
            return timeline;
        }

        public async Task<AwdCompetitionDeadline> DeadlineAsync(Guid competitionId, CancellationToken ct)
        {
            if (!deadlines.TryGetValue(competitionId, out var deadline))
            {
                var competition = Competitions[competitionId];
                deadline = await AwdCompetitionDeadline.ReadAsync(db, competitionId, competition.StartAt, competition.EndAt,
                    (AwdCompetitionModeConfiguration)competition.ModeConfiguration!, now, ct);
                deadlines.Add(competitionId, deadline);
                timelines.TryAdd(competitionId, deadline.Clock);
            }
            return deadline;
        }
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
