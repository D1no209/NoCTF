using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scheduling;
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
    TimeSpan Timeout);

public interface IAwdFlagInjectionWorkReader
{
    Task<AwdFlagInjectionWork?> ReadAsync(InjectAwdFlag message, CancellationToken cancellationToken);
}

public interface IAwdFlagInjectionExecutor
{
    Task<ContainerExecResult> ExecuteAsync(AwdFlagInjectionWork work, CancellationToken cancellationToken);
}

public sealed class AwdFlagInjectionWorkReader(
    IDbContextFactory<NoCtfDbContext> contexts,
    IAwdFlagInjectionConfigurationCatalog configurations) : IAwdFlagInjectionWorkReader
{
    public async Task<AwdFlagInjectionWork?> ReadAsync(
        InjectAwdFlag message,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.Id == message.RuntimeInstanceId
                && runtime.CompetitionChallengeId == message.CompetitionChallengeId
                && runtime.State == RuntimeState.Running
                && runtime.RunnerId == message.RunnerId
                && runtime.ProviderReceipt != null)
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                runtime => runtime.CompetitionChallengeId,
                challenge => challenge.Id,
                (runtime, challenge) => new { Runtime = runtime, Challenge = challenge })
            .Join(
                db.ChallengeFlags.AsNoTracking(),
                pair => pair.Runtime.TeamId,
                flag => flag.TeamId,
                (pair, flag) => new { pair.Runtime, pair.Challenge, Flag = flag })
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.Challenge.ChallengeId,
                template => template.Id,
                (item, template) => new { item.Runtime, item.Challenge, item.Flag, Template = template })
            .Where(target => target.Flag.Id == message.ChallengeFlagId
                && target.Flag.CompetitionChallengeId == message.CompetitionChallengeId
                && target.Flag.SpecificationKind == SpecificationKind.AwdRound
                && target.Flag.ValidUntil == message.ValidUntil
                && target.Flag.DeletedAt == null)
            .Select(target => new
            {
                target.Runtime.CompetitionId,
                target.Runtime.RuntimeKind,
                target.Runtime.RuntimeProvider,
                ProviderReceipt = target.Runtime.ProviderReceipt!,
                target.Flag.Flag,
                target.Template.Definition
            })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
            return null;
        if (target.Definition is not AwdChallengeDefinition definition)
            return null;
        var injection = configurations.Get(definition);
        if (injection is null)
            return null;
        return new(
            target.CompetitionId!.Value,
            target.RuntimeKind,
            target.RuntimeProvider,
            target.ProviderReceipt.ToData(),
            target.Flag,
            injection.Command,
            injection.ServiceName,
            TimeSpan.FromSeconds(injection.TimeoutSeconds));
    }
}

public sealed class AwdFlagInjectionExecutor(IRuntimeProviderCatalog providers)
    : IAwdFlagInjectionExecutor
{
    public Task<ContainerExecResult> ExecuteAsync(
        AwdFlagInjectionWork work,
        CancellationToken cancellationToken)
    {
        var expanded = work.CommandTemplate.Replace(
            "${FLAG}",
            work.Flag,
            StringComparison.Ordinal);
        IReadOnlyList<string> command = ["/bin/sh", "-c", expanded];
        return work.RuntimeKind switch
        {
            RuntimeKind.Container => providers.Sandbox(work.RuntimeProvider).ExecAsync(
                (work.ProviderReceipt as ContainerRuntimeReceiptData)?.ToReceipt()
                    ?? throw new InvalidDataException("Container receipt is invalid."),
                command,
                work.Timeout,
                cancellationToken),
            RuntimeKind.Compose => providers.Compose(work.RuntimeProvider).ExecAsync(
                (work.ProviderReceipt as ComposeRuntimeReceiptData)?.ToReceipt()
                    ?? throw new InvalidDataException("Compose receipt is invalid."),
                work.ServiceName
                    ?? throw new InvalidDataException("Compose flag injection requires a service name."),
                command,
                work.Timeout,
                cancellationToken),
            _ => throw new NotSupportedException("AWD flag injection only supports Container and Compose runtimes.")
        };
    }

}

[NonTransactional]
public sealed class AwdFlagInjectionHandler(
    IAwdFlagInjectionWorkReader reader,
    IAwdFlagInjectionExecutor executor,
    IPostCommitMessagePublisher outbox,
    IOptions<RunnerOptions> runnerOptions,
    TimeProvider timeProvider)
{
    public async Task<MessageExecutionOutcome> Handle(
        InjectAwdFlag message,
        CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            runnerOptions.Value.Pool,
            runnerOptions.Value.Id);
        var work = await reader.ReadAsync(message, cancellationToken);
        if (work is null)
            return MessageExecutionOutcome.Superseded;

        var now = timeProvider.GetUtcNow();
        if (now >= message.ValidUntil)
            return await RecordFinalFailureAsync(message, work.CompetitionId, now);
        ContainerExecResult result;
        try
        {
            result = await executor.ExecuteAsync(work, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await RetryOrRecordFailureAsync(message, work.CompetitionId);
        }
        if (!result.TimedOut && result.ExitCode == 0)
            return MessageExecutionOutcome.Applied;
        return await RetryOrRecordFailureAsync(message, work.CompetitionId);
    }

    private async Task<MessageExecutionOutcome> RetryOrRecordFailureAsync(
        InjectAwdFlag message,
        Guid competitionId)
    {
        var now = timeProvider.GetUtcNow();
        var nextAt = AwdRoundScheduler.NextInjectionAttempt(
            now,
            message.ValidUntil,
            message.FailedAttempts);
        if (nextAt is DateTimeOffset retryAt)
        {
            await outbox.ScheduleToRunnerNodeAsync(
                message with { FailedAttempts = checked(message.FailedAttempts + 1) },
                retryAt);
            await outbox.FlushOutgoingMessagesAsync();
            return MessageExecutionOutcome.DeferredSchedule;
        }

        return await RecordFinalFailureAsync(message, competitionId, now);
    }

    private async Task<MessageExecutionOutcome> RecordFinalFailureAsync(
        InjectAwdFlag message,
        Guid competitionId,
        DateTimeOffset now)
    {
        await outbox.PublishAsync(new AwdFlagInjectionFailed(
            competitionId,
            message.CompetitionChallengeId,
            message.RuntimeInstanceId,
            message.ChallengeFlagId,
            now));
        await outbox.FlushOutgoingMessagesAsync();
        return MessageExecutionOutcome.Applied;
    }
}
