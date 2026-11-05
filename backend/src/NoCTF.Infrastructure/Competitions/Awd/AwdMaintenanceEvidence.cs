using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

public static class AwdMaintenanceEvidence
{
    public static async Task RecordFlagWaitAsync(NoCtfDbContext db, InjectAwdFlag message, DateTimeOffset startedAt,
        DateTimeOffset endedAt, CancellationToken ct)
    {
        if (startedAt >= message.ValidUntil || endedAt < message.ValidUntil) return;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var issue = await db.AwdFlagIssues.SingleOrDefaultAsync(value => value.FlagId == message.ChallengeFlagId
            && value.CompetitionChallengeId == message.CompetitionChallengeId && value.DeliveryRuntimeId == message.RuntimeInstanceId
            && value.State == AwdFlagIssueState.Issued && value.DeliveryState == AwdFlagDeliveryState.Pending, ct);
        if (issue is null || !await db.AwdRounds.AnyAsync(round => round.Id == issue.RoundId && round.EndsAt == message.ValidUntil, ct)) return;
        var fix = await db.GameplayFacts.AsNoTracking().AsSplitQuery().Where(value => value.Kind == GameplayFactKind.FixAttempt
                && value.CompetitionId == issue.CompetitionId && value.CompetitionChallengeId == issue.CompetitionChallengeId && value.TeamId == issue.TeamId
                && value.FixExecution != null && value.FixExecution.TargetKind == FixTargetKind.Persistent
                && value.FixExecution.RuntimeInstanceId == message.RuntimeInstanceId && value.FixExecution.MutationStartedAt <= message.ValidUntil
                && (value.FixExecution.MutationCompletedAt >= message.ValidUntil || value.FixExecution.LastConfirmedRunningAt >= message.ValidUntil))
            .OrderBy(value => value.FixExecution!.MutationStartedAt).FirstOrDefaultAsync(ct);
        if (fix is null) return;
        issue.BlockingFixFactId = fix.Id; issue.MaintenanceWaitStartedAt = startedAt; issue.MaintenanceWaitEndedAt = endedAt;
        issue.DeliveryState = AwdFlagDeliveryState.Unconfirmed;
        issue.DeliveryReason = fix.State == GameplayFactState.Completed && fix.FixExecution is { MutationBlocked: false }
            ? AwdCheckReason.PlayerMaintenance : fix.State == GameplayFactState.PlatformFailed
                ? AwdCheckReason.InfrastructureFailure : AwdCheckReason.TaskNotExecuted;
        issue.UpdatedAt = endedAt; issue.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public static async Task ReconcileFlagWaitsAsync(NoCtfDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var pending = await db.AwdFlagIssues.Where(issue => issue.BlockingFixFactId != null
            && issue.DeliveryReason == AwdCheckReason.TaskNotExecuted).OrderBy(issue => issue.FlagId).Take(500).ToArrayAsync(ct);
        var ids = pending.Select(issue => issue.BlockingFixFactId!.Value).Distinct().ToArray();
        var fixes = await db.GameplayFacts.AsNoTracking().AsSplitQuery().Where(fact => ids.Contains(fact.Id)).ToDictionaryAsync(fact => fact.Id, ct);
        foreach (var issue in pending)
        {
            var fix = fixes[issue.BlockingFixFactId!.Value];
            if (fix.State == GameplayFactState.PlatformFailed) issue.DeliveryReason = AwdCheckReason.InfrastructureFailure;
            else if (fix.State == GameplayFactState.Completed && fix.FixExecution is { MutationBlocked: false }) issue.DeliveryReason = AwdCheckReason.PlayerMaintenance;
            else continue;
            issue.UpdatedAt = now; issue.ConcurrencyStamp = Guid.NewGuid();
        }
    }

    public static async Task RecordCheckerWaitAsync(NoCtfDbContext db, RunAwdChecker message, DateTimeOffset startedAt,
        DateTimeOffset endedAt, CancellationToken ct)
    {
        if (startedAt >= message.Deadline || endedAt < message.Deadline) return;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var fact = await db.GameplayFacts.Include(value => value.Awd).AsSplitQuery().SingleOrDefaultAsync(value => value.Id == message.GameplayFactId, ct);
        if (fact?.Awd is not { } details || details.AttemptId != message.AttemptId || details.RuntimeId != message.RuntimeInstanceId
            || details.Deadline != message.Deadline || details.AttemptStartedAt is not null || details.AdjudicatedAt is not null
            || details.Settlement is AwdSettlementState.Final or AwdSettlementState.Exempted) return;
        var blocking = await db.GameplayFacts.AsNoTracking().Where(value => value.Kind == GameplayFactKind.FixAttempt
                && value.CompetitionId == fact.CompetitionId && value.CompetitionChallengeId == fact.CompetitionChallengeId && value.TeamId == fact.TeamId
                && value.FixExecution != null && value.FixExecution.TargetKind == FixTargetKind.Persistent
                && value.FixExecution.RuntimeInstanceId == message.RuntimeInstanceId && value.FixExecution.MutationStartedAt <= message.Deadline
                && (value.FixExecution.MutationCompletedAt >= message.Deadline || value.FixExecution.LastConfirmedRunningAt >= message.Deadline))
            .OrderBy(value => value.FixExecution!.MutationStartedAt).Select(value => (Guid?)value.Id).FirstOrDefaultAsync(ct);
        if (blocking is null) return;
        details.BlockingFixFactId = blocking;
        details.MaintenanceWaitStartedAt = startedAt; details.MaintenanceWaitEndedAt = endedAt;
        fact.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public static bool CoversDeadline(AwdFactDetails check, GameplayFact fix) => check.Deadline is { } deadline
        && check.MaintenanceWaitStartedAt < deadline && check.MaintenanceWaitEndedAt >= deadline
        && fix.Id == check.BlockingFixFactId && fix.FixExecution is { TargetKind: FixTargetKind.Persistent } execution
        && execution.RuntimeInstanceId == check.RuntimeId && execution.MutationStartedAt <= deadline
        && (execution.MutationCompletedAt >= deadline || execution.LastConfirmedRunningAt >= deadline);
}
