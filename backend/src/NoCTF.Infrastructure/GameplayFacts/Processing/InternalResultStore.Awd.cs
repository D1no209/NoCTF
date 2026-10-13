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

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed partial class InternalResultStore
{
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
        if (fact is not { Kind: GameplayFactKind.AwdServiceTransition, Awd: { } details })
            return InternalResultDisposition.NotFound;
        if (details.RuntimeId != result.RuntimeInstanceId || details.AttemptId != result.AttemptId)
            return InternalResultDisposition.Superseded;
        if (details.ReceivedAt is not null || details.Execution == AwdCheckExecution.Completed
            || details.Settlement is AwdSettlementState.Final or AwdSettlementState.Exempted)
            return InternalResultDisposition.Duplicate;
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
                    issue.ProtectedReadContext = Convert.ToBase64String((secrets ?? throw new InvalidOperationException("AWD context protection is unavailable."))
                        .Protect(context, PlatformSecretPurpose.AwdFlagReadContext, issue.CompetitionId, issue.FlagId));
                if (flag.RequiresWrite && report.PublicLocator is { } locator)
                    issue.PublicLocator = locator;
            }
            conclusion = AwdHealthEvaluator.Evaluate(new(result.Connection, result.Functionality,
                details.Flags.Select(flag => new AwdFlagObservation(flag.FlagId, flag.Historical, flag.Obligation,
                    new(flag.Write, flag.WriteReason), new(flag.Read, flag.ReadReason))).ToArray()));
        }
        details.Connection = result.Connection.Outcome;
        details.ConnectionReason = result.Connection.Reason;
        details.Functionality = result.Functionality.Outcome;
        details.FunctionalityReason = result.Functionality.Reason;
        details.ObservedAt = result.ObservedAt;
        details.ReceivedAt = result.ReceivedAt;
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
        await events.RecordAsync(new(fact.CompetitionId, CompetitionEventKind.ScoringRecorded,
            conclusion.Health == AwdHealth.Ok ? CompetitionEventLevel.Information : CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Team, result.ReceivedAt, TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId, RuntimeInstanceId: result.RuntimeInstanceId,
            GameplayFactId: fact.Id, GameplayFactKind: fact.Kind, GameplayFactState: fact.State,
            GameplayFactResult: fact.Result), ct);
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        if (details.Settlement == AwdSettlementState.Provisional)
            await outbox.ScheduleAsync(new DispatchAwdCheckers(result.ReceivedAt.AddSeconds(rules.Options.RetryIntervalSeconds)),
                result.ReceivedAt.AddSeconds(rules.Options.RetryIntervalSeconds));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return InternalResultDisposition.Applied;
    }
}
