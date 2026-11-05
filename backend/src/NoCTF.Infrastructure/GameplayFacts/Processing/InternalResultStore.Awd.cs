using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Checking;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Infrastructure.Challenges.Capabilities;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed partial class InternalResultStore
{
    public async Task<InternalResultDisposition> RecordAwdCapabilityCheckAsync(AwdCapabilityCheckResult result, CancellationToken ct)
    {
        var fact = await db.GameplayFacts.AsNoTracking().Include(value => value.Awd).ThenInclude(value => value!.Flags)
            .SingleOrDefaultAsync(value => value.Id == result.GameplayFactId, ct);
        if (fact is not { Kind: GameplayFactKind.CheckObservation, Awd: { } details }) return InternalResultDisposition.NotFound;
        if (details.RuntimeId != result.RuntimeInstanceId || details.AttemptId != result.AttemptId) return InternalResultDisposition.Superseded;
        var mapped = AwdCapabilityEvidence.Map(result, details.Flags.Select(flag => new AwdExpectedFlag(flag.FlagId,
            flag.Historical, flag.Obligation, flag.RequiresWrite)).ToArray());
        return mapped.Succeeded ? await RecordAwdAsync(mapped.Value!, ct) : InternalResultDisposition.Conflict;
    }

    public async Task<InternalResultDisposition> RecordAwdAsync(AwdCheckResult result, CancellationToken ct)
    {
        for (var retry = 0; ; retry++)
        {
            try { return await RecordAwdCoreAsync(result, ct); }
            catch (Exception exception) when (retry < 2 && TransactionFailureClassifier.IsRetryable(exception))
            {
                outbox.DiscardPendingMessages();
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<InternalResultDisposition> RecordAwdCoreAsync(AwdCheckResult result, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var fact = await db.GameplayFacts.Include(item => item.Awd).ThenInclude(details => details!.Flags)
            .SingleOrDefaultAsync(item => item.Id == result.GameplayFactId, ct);
        if (fact is not { Kind: GameplayFactKind.CheckObservation, Awd: { } details })
            return InternalResultDisposition.NotFound;
        if (details.RuntimeId != result.RuntimeInstanceId || details.AttemptId != result.AttemptId)
            return InternalResultDisposition.Superseded;
        if (details.ReceivedAt is not null || details.Execution == AwdCheckExecution.Completed)
            return SameAwdEvidence(fact, result) ? InternalResultDisposition.Duplicate : InternalResultDisposition.Conflict;
        if (details.Settlement == AwdSettlementState.Exempted)
            return InternalResultDisposition.Duplicate;
        if (details.Settlement == AwdSettlementState.Final)
            return SameAwdEvidence(fact, result) ? InternalResultDisposition.Duplicate : InternalResultDisposition.Superseded;
        if (details.Execution is not (AwdCheckExecution.Running or AwdCheckExecution.FailedToDetermine) || details.AttemptStartedAt is not { } started
            || details.AttemptDeadline is not { } deadline || details.ReceiptDeadline is not { } receiptDeadline)
            return InternalResultDisposition.Conflict;
        var late = result.ReceivedAt > receiptDeadline;
        if (result.ObservedAt < started || result.ObservedAt > deadline || result.ObservedAt > result.ReceivedAt)
            return InternalResultDisposition.Conflict;
        if (!await db.RuntimeInstances.AnyAsync(runtime => runtime.Id == result.RuntimeInstanceId
                && runtime.CompetitionId == fact.CompetitionId && runtime.CompetitionChallengeId == fact.CompetitionChallengeId
                && runtime.TeamId == fact.TeamId, ct))
            return InternalResultDisposition.NotFound;

        var expected = details.Flags.Select(flag => new AwdExpectedFlag(flag.FlagId, flag.Historical,
            flag.Obligation, flag.RequiresWrite)).ToArray();
        var validated = AwdHealthEvaluator.ValidateManifest(result.Connection, result.Functionality, expected, result.Flags);
        details.EvidenceComplete = validated.Evidence is not null;
        var conclusion = validated.Conclusion;
        if (validated.Evidence is not null)
        {
            if (result.Flags.Any(flag => flag.PrivateReadContext?.Length > 8192 || flag.PublicLocator?.Length > 2048))
                return InternalResultDisposition.Conflict;
            var ids = expected.Select(flag => flag.FlagId).ToArray();
            var issues = await db.AwdFlagIssues.Where(issue => ids.Contains(issue.FlagId)).ToDictionaryAsync(issue => issue.FlagId, ct);
            var flagState = await db.ChallengeFlags.Where(flag => ids.Contains(flag.Id)).ToDictionaryAsync(flag => flag.Id, ct);
            foreach (var report in result.Flags)
            {
                var flag = details.Flags.Single(item => item.FlagId == report.FlagId);
                var issue = issues[flag.FlagId];
                if (issue.CompetitionId != fact.CompetitionId || issue.CompetitionChallengeId != fact.CompetitionChallengeId
                    || issue.TeamId != fact.TeamId)
                    return InternalResultDisposition.Conflict;
                // Only execution of a required write, or an actual successful read, establishes custody.
                // An unconfirmed write followed by a missing read is still a platform uncertainty.
                if (issue.Obligation != AwdFlagObligation.Exempted
                    && (report.Read.Outcome == AwdProbeOutcome.Passed
                        || flag.RequiresWrite && report.Write.Outcome is AwdProbeOutcome.Passed or AwdProbeOutcome.TargetFailure))
                {
                    issue.Obligation = AwdFlagObligation.PlayerRequired;
                    issue.DeliveryState = report.Read.Outcome == AwdProbeOutcome.Passed || report.Write.Outcome == AwdProbeOutcome.Passed
                        ? AwdFlagDeliveryState.Succeeded : AwdFlagDeliveryState.TargetFailed;
                    issue.DeliveryReason = report.Write.Reason;
                    issue.UpdatedAt = result.ReceivedAt;
                }
                flag.Obligation = issue.Obligation;
                flag.Write = flag.RequiresWrite ? report.Write.Outcome : AwdProbeOutcome.NotRequired;
                flag.WriteReason = flag.RequiresWrite ? report.Write.Reason : AwdCheckReason.None;
                flag.Read = report.Read.Outcome;
                flag.ReadReason = report.Read.Reason;
                if (flag.RequiresWrite && report.PrivateReadContext is { } context)
                    flagState[flag.FlagId].ProtectedReadContext = (secrets ?? throw new InvalidOperationException("Flag context protection is unavailable."))
                        .Protect(context, PlatformSecretPurpose.FlagReadContext, issue.CompetitionId, issue.FlagId);
                if (flag.RequiresWrite && report.PublicLocator is { } locator)
                    flagState[flag.FlagId].PublicLocator = locator;
            }
            conclusion = AwdHealthEvaluator.Evaluate(new(result.Connection, result.Functionality,
                details.Flags.Select(flag => new AwdFlagObservation(flag.FlagId, flag.Historical, flag.Obligation,
                    new(flag.Write, flag.WriteReason), new(flag.Read, flag.ReadReason))).ToArray()));
            if (issues.Values.Any(issue => issue.RoundId == details.ReceivedRoundId && issue.DeliveryReason == AwdCheckReason.PlayerMaintenance
                    && issue.BlockingFixFactId != null))
                conclusion = conclusion with { Health = AwdHealth.Offline, Reason = AwdCheckReason.PlayerMaintenance };
        }
        details.Connection = result.Connection.Outcome;
        details.ConnectionReason = result.Connection.Reason;
        details.Functionality = result.Functionality.Outcome;
        details.FunctionalityReason = result.Functionality.Reason;
        details.ObservedAt = result.ObservedAt;
        details.ReceivedAt = result.ReceivedAt;
        AwdCheckCapabilityEvidence.Apply(fact, result.ObservedAt);
        var outcome = fact.CapabilityOutcomes.OfType<CheckOutcome>().Single();
        var reportedContexts = result.Flags.Where(flag => flag.PrivateReadContext is not null || flag.PublicLocator is not null).ToArray();
        outcome.FlagContexts.RemoveAll(context => reportedContexts.All(flag => flag.FlagId != context.FlagId));
        foreach (var report in reportedContexts)
        {
            var context = outcome.FlagContexts.SingleOrDefault(value => value.FlagId == report.FlagId);
            if (context is null) { context = new() { GameplayFactId = fact.Id, FlagId = report.FlagId }; outcome.FlagContexts.Add(context); }
            context.ProtectedReadContext = CheckFlagContextProtection.Protect(report.PrivateReadContext, fact.Id, report.FlagId, secrets);
            context.PublicLocator = report.PublicLocator;
        }
        if (!details.ManifestComplete)
            conclusion = conclusion with { Health = AwdHealth.InternalError, Reason = AwdCheckReason.IncompleteEvidence };
        // A late report is evidence for adjudication, never an automatic historical SLA correction.
        if (late)
            conclusion = conclusion with { Health = AwdHealth.InternalError, Reason = AwdCheckReason.LateReceipt };
        details.Health = conclusion.Health;
        details.Reason = conclusion.Reason;
        details.KnownTargetFailure = conclusion.KnownTargetFailure ?? details.KnownTargetFailure;
        details.RequiredFlags = conclusion.RequiredFlags;
        details.ReadableFlags = conclusion.ReadableFlags;
        details.Execution = conclusion.IsConclusive ? AwdCheckExecution.Completed : AwdCheckExecution.FailedToDetermine;
        var rules = await db.AwdRoundRules.AsNoTracking().SingleAsync(item => item.Id == details.RulesId, ct);
        details.Settlement = conclusion.IsConclusive ? AwdSettlementState.Final
            : !late && details.Attempt <= rules.Options.RetryCount
                && result.ReceivedAt.AddSeconds(rules.Options.RetryIntervalSeconds) < details.Deadline
                    ? AwdSettlementState.Provisional : AwdSettlementState.PendingAdjudication;
        fact.Result = AwdCheckerOutcomeMapper.ToGameplayFactResult(conclusion.Health);
        fact.State = conclusion.IsConclusive ? GameplayFactState.Completed : GameplayFactState.PlatformFailed;
        fact.FailureCode = conclusion.IsConclusive ? null : GameplayFactFailureCode.CheckerPlatformError;
        fact.UpdatedAt = result.ReceivedAt;
        fact.ConcurrencyStamp = Guid.NewGuid();
        if (details.Settlement == AwdSettlementState.PendingAdjudication)
            await AwdAdjudicationNotification.AddAsync(db, fact, result.ReceivedAt, ct);
        await events.RecordAsync(new(fact.CompetitionId!.Value, CompetitionEventKind.ScoringRecorded,
            conclusion.Health == AwdHealth.Ok ? CompetitionEventLevel.Information : CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Team, result.ReceivedAt, TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId, RuntimeInstanceId: result.RuntimeInstanceId,
            GameplayFactId: fact.Id, GameplayFactKind: fact.Kind, GameplayFactState: fact.State,
            GameplayFactResult: fact.Result), ct);
        if (details.Settlement == AwdSettlementState.Provisional)
            await outbox.ScheduleAsync(new DispatchAwdCheckers(result.ReceivedAt.AddSeconds(rules.Options.RetryIntervalSeconds)),
                result.ReceivedAt.AddSeconds(rules.Options.RetryIntervalSeconds));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
        await outbox.FlushCommittedMessagesAsync();
        return InternalResultDisposition.Applied;
    }

    private bool SameAwdEvidence(GameplayFact fact, AwdCheckResult report)
    {
        var details = fact.Awd!;
        if (details.ObservedAt != report.ObservedAt || details.Connection != report.Connection.Outcome
            || details.ConnectionReason != report.Connection.Reason || details.Functionality != report.Functionality.Outcome
            || details.FunctionalityReason != report.Functionality.Reason || report.Flags.Count != details.Flags.Count
            || report.Flags.Select(flag => flag.FlagId).Distinct().Count() != report.Flags.Count) return false;
        var contexts = fact.CapabilityOutcomes.OfType<CheckOutcome>().SingleOrDefault()?.FlagContexts ?? [];
        foreach (var flag in report.Flags)
        {
            var previous = details.Flags.SingleOrDefault(value => value.FlagId == flag.FlagId);
            if (previous is null || previous.RequiresWrite && (previous.Write != flag.Write.Outcome || previous.WriteReason != flag.Write.Reason)
                || previous.Read != flag.Read.Outcome || previous.ReadReason != flag.Read.Reason) return false;
            var context = contexts.SingleOrDefault(value => value.FlagId == flag.FlagId);
            if (!string.Equals(context?.PublicLocator, flag.PublicLocator, StringComparison.Ordinal)
                || !string.Equals(CheckFlagContextProtection.Reveal(context?.ProtectedReadContext, fact.Id, flag.FlagId, secrets),
                    flag.PrivateReadContext, StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
