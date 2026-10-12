using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using Wolverine.Attributes;

namespace NoCTF.Runner.Messages;

public sealed record AwdFlagInjectionWork(
    Guid CompetitionId,
    RuntimeKind RuntimeKind,
    RuntimeProvider RuntimeProvider,
    RuntimeReceiptData ProviderReceipt,
    string Flag,
    string CommandTemplate,
    string? ServiceName,
    TimeSpan Timeout,
    Guid FlagId = default,
    Guid StoreId = default,
    Guid AttemptId = default);

public interface IAwdFlagInjectionWorkReader
{
    Task<AwdFlagInjectionWork?> ReadAsync(InjectAwdFlag message, CancellationToken cancellationToken);
    Task CompleteAsync(InjectAwdFlag message, AwdFlagInjectionWork work, AwdFlagDeliveryState state,
        AwdCheckReason reason, CancellationToken cancellationToken);
}

public interface IAwdFlagInjectionExecutor
{
    Task<ContainerExecResult> ExecuteAsync(AwdFlagInjectionWork work, CancellationToken cancellationToken);
}

public sealed class AwdFlagInjectionWorkReader(
    IDbContextFactory<NoCtfDbContext> contexts,
    TimeProvider timeProvider) : IAwdFlagInjectionWorkReader
{
    public async Task<AwdFlagInjectionWork?> ReadAsync(InjectAwdFlag message, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var target = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.Id == message.RuntimeInstanceId
                && runtime.CompetitionChallengeId == message.CompetitionChallengeId
                && runtime.State == RuntimeState.Running && runtime.RunnerId == message.RunnerId
                && runtime.ProviderReceipt != null)
            .Join(db.ChallengeFlags.AsNoTracking(), runtime => runtime.TeamId, flag => flag.TeamId,
                (runtime, flag) => new { Runtime = runtime, Flag = flag })
            .Where(item => item.Flag.Id == message.ChallengeFlagId
                && item.Flag.CompetitionChallengeId == message.CompetitionChallengeId && item.Flag.DeletedAt == null)
            .Join(db.AwdFlagIssues, item => item.Flag.Id, issue => issue.FlagId,
                (item, issue) => new { item.Runtime, item.Flag, Issue = issue })
            .Join(db.AwdRoundRules, item => item.Issue.RulesId, rules => rules.Id,
                (item, rules) => new { item.Runtime, item.Flag, item.Issue, Rules = rules })
            .Join(db.AwdRounds, item => item.Issue.RoundId, round => round.Id,
                (item, round) => new { item.Runtime, item.Flag, item.Issue, item.Rules, Round = round })
            .Join(db.Competitions, item => item.Issue.CompetitionId, competition => competition.Id,
                (item, competition) => new { item.Runtime, item.Flag, item.Issue, item.Rules, item.Round, Competition = competition })
            .Where(item => item.Competition.Status == CompetitionStatus.Running
                && item.Competition.EndAt > now && item.Round.StartsAt <= now && item.Round.EndsAt > now
                && item.Round.EndsAt == message.ValidUntil && item.Issue.State == AwdFlagIssueState.Issued
                && item.Issue.DeliveryState == AwdFlagDeliveryState.Pending
                && item.Issue.DeliveryRuntimeId == message.RuntimeInstanceId)
            .AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
        if (target is null) return null;
        var store = target.Rules.Stores.Single(store => store.StoreId == target.Issue.StoreId);
        if (store.DeliveryMode != AwdFlagDeliveryMode.RunnerCommand || string.IsNullOrWhiteSpace(store.InjectionCommand))
            return null;
        var attemptId = Guid.CreateVersion7();
        var claimed = await db.AwdFlagIssues.Where(issue => issue.FlagId == message.ChallengeFlagId
                && issue.ConcurrencyStamp == target.Issue.ConcurrencyStamp && issue.DeliveryState == AwdFlagDeliveryState.Pending
                && issue.DeliveryRuntimeId == message.RuntimeInstanceId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(issue => issue.DeliveryState, AwdFlagDeliveryState.Running)
                .SetProperty(issue => issue.DeliveryAttemptId, attemptId)
                .SetProperty(issue => issue.DeliveryAttempts, issue => issue.DeliveryAttempts + 1)
                .SetProperty(issue => issue.UpdatedAt, now)
                .SetProperty(issue => issue.ConcurrencyStamp, Guid.NewGuid()), cancellationToken);
        if (claimed == 0) return null;
        return new(target.Issue.CompetitionId, target.Runtime.RuntimeKind, target.Runtime.RuntimeProvider,
            target.Runtime.ProviderReceipt!.ToData(), target.Flag.Flag, store.InjectionCommand,
            store.TargetServiceName, TimeSpan.FromSeconds(Math.Min(store.InjectionTimeoutSeconds,
                (message.ValidUntil - now).TotalSeconds)), target.Flag.Id, store.StoreId, attemptId);
    }

    public async Task CompleteAsync(InjectAwdFlag message, AwdFlagInjectionWork work, AwdFlagDeliveryState state,
        AwdCheckReason reason, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        // A stale completion cannot overwrite a different runtime/attempt or a checker's confirmed read.
        await db.AwdFlagIssues.Where(issue => issue.FlagId == message.ChallengeFlagId
                && issue.DeliveryRuntimeId == message.RuntimeInstanceId && issue.DeliveryAttemptId == work.AttemptId
                && issue.DeliveryState == AwdFlagDeliveryState.Running)
            .ExecuteUpdateAsync(setters => setters.SetProperty(issue => issue.DeliveryState, state)
                .SetProperty(issue => issue.DeliveryReason, reason)
                .SetProperty(issue => issue.Obligation, state == AwdFlagDeliveryState.Succeeded
                    ? AwdFlagObligation.PlayerRequired : AwdFlagObligation.PlatformPending)
                .SetProperty(issue => issue.UpdatedAt, now)
                .SetProperty(issue => issue.ConcurrencyStamp, Guid.NewGuid()), cancellationToken);
    }
}

public sealed class AwdFlagInjectionExecutor(IRuntimeProviderCatalog providers) : IAwdFlagInjectionExecutor
{
    public Task<ContainerExecResult> ExecuteAsync(AwdFlagInjectionWork work, CancellationToken cancellationToken)
    {
        // Secret values are shell arguments, never shell source. Disable globbing for unquoted legacy expansions.
        IReadOnlyList<string> command = ["/bin/sh", "-c",
            "set -f; FLAG=$1; FLAG_ID=$2; STORE_ID=$3; export FLAG FLAG_ID STORE_ID; " + work.CommandTemplate,
            "noctf-flag", work.Flag, work.FlagId.ToString("N"), work.StoreId.ToString("N")];
        return work.RuntimeKind switch
        {
            RuntimeKind.Container => providers.Runtime(work.RuntimeProvider).ExecAsync(
                (work.ProviderReceipt as ContainerRuntimeReceiptData)?.ToReceipt()
                    ?? throw new InvalidDataException("Container receipt is invalid."),
                work.ServiceName ?? throw new InvalidDataException("Flag injection requires a service name."),
                command, work.Timeout, cancellationToken),
            _ => throw new NotSupportedException("AWD flag injection only supports Container runtimes.")
        };
    }
}

[NonTransactional]
public sealed class AwdFlagInjectionHandler(
    IAwdFlagInjectionWorkReader reader,
    IAwdFlagInjectionExecutor executor,
    IPostCommitMessagePublisher outbox,
    IOptions<RunnerOptions> runnerOptions,
    TimeProvider timeProvider,
    ILogger<AwdFlagInjectionHandler>? logger = null)
{
    public async Task<MessageExecutionOutcome> Handle(InjectAwdFlag message, CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(message, runnerOptions.Value.Pool, runnerOptions.Value.Id);
        var work = await reader.ReadAsync(message, cancellationToken);
        if (work is null) return MessageExecutionOutcome.Superseded;
        var state = AwdFlagDeliveryState.Unconfirmed;
        var reason = AwdCheckReason.TaskNotExecuted;
        if (timeProvider.GetUtcNow() < message.ValidUntil)
        {
            try
            {
                var result = await executor.ExecuteAsync(work, cancellationToken);
                if (!result.TimedOut && result.ExitCode == 0)
                {
                    state = AwdFlagDeliveryState.Succeeded;
                    reason = AwdCheckReason.None;
                }
                else reason = result.TimedOut ? AwdCheckReason.CheckerTimedOut : AwdCheckReason.FlagStoreFailed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                reason = AwdCheckReason.InfrastructureFailure;
                logger?.LogWarning("AWD Flag injection provider failed with {FailureType} for Runtime {RuntimeInstanceId}.",
                    exception.GetType().Name, message.RuntimeInstanceId);
            }
        }
        await reader.CompleteAsync(message, work, state, reason, cancellationToken);
        if (state != AwdFlagDeliveryState.Succeeded)
        {
            // A command can have written successfully before losing its acknowledgement.
            // Keep the same issuance for checker verification; never blindly repeat a destructive write.
            await outbox.PublishAsync(new AwdFlagInjectionFailed(work.CompetitionId, message.CompetitionChallengeId,
                message.RuntimeInstanceId, message.ChallengeFlagId, timeProvider.GetUtcNow()));
            await outbox.FlushOutgoingMessagesAsync();
        }
        return MessageExecutionOutcome.Applied;
    }
}
