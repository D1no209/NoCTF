using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    string ProviderReceiptJson,
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
    IServiceScopeFactory scopes,
    AwdFlagInjectionConfigurationCatalog configurations) : IAwdFlagInjectionWorkReader
{
    public async Task<AwdFlagInjectionWork?> ReadAsync(
        InjectAwdFlag message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var target = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.Id == message.RuntimeInstanceId
                && runtime.CompetitionChallengeId == message.CompetitionChallengeId
                && runtime.Generation == message.Generation
                && runtime.ProcessingVersion == message.ProcessingVersion
                && runtime.State == RuntimeState.Running
                && runtime.RunnerPool == message.RunnerPool
                && runtime.RunnerId == message.RunnerId
                && runtime.ProviderReceiptJson != null)
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
                ProviderReceiptJson = target.Runtime.ProviderReceiptJson!,
                target.Flag.Flag,
                target.Template.DefinitionJson
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
            return null;
        var injection = configurations.Get(target.DefinitionJson);
        if (injection is null)
            return null;
        return new(
            target.CompetitionId,
            target.RuntimeKind,
            target.RuntimeProvider,
            target.ProviderReceiptJson,
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
                JsonSerializer.Deserialize<ContainerReceipt>(work.ProviderReceiptJson)
                    ?? throw new InvalidDataException("Container receipt is invalid."),
                command,
                work.Timeout,
                cancellationToken),
            RuntimeKind.Compose => providers.Compose(work.RuntimeProvider).ExecAsync(
                JsonSerializer.Deserialize<ComposeReceipt>(work.ProviderReceiptJson)
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
    ITransactionalMessageOutbox outbox,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    public Task Handle(InjectAwdFlag message, CancellationToken cancellationToken) =>
        ExecuteAsync(message, cancellationToken);

    public async Task<MessageExecutionOutcome> ExecuteAsync(
        InjectAwdFlag message,
        CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            configuration["Runner:Pool"] ?? "default",
            configuration["Runner:Id"]
                ?? throw new InvalidOperationException("Runner:Id is required."));
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
            message.Generation,
            message.ProcessingVersion,
            now));
        await outbox.FlushOutgoingMessagesAsync();
        return MessageExecutionOutcome.Applied;
    }
}
