using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using Wolverine.Attributes;

namespace NoCTF.Runner.Messages;

public interface IChallengeTestFlagInjectionStore
{
    Task<AwdFlagInjectionWork?> ReadAsync(
        InjectChallengeTestFlag message,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        InjectChallengeTestFlag message,
        RuntimeTestFlagState state,
        CancellationToken cancellationToken);
}

public sealed class ChallengeTestFlagInjectionStore(
    IDbContextFactory<NoCtfDbContext> contexts,
    NoCTF.GameModes.Awd.Configuration.IAwdFlagInjectionConfigurationCatalog configurations,
    TimeProvider timeProvider) : IChallengeTestFlagInjectionStore
{
    public async Task<AwdFlagInjectionWork?> ReadAsync(
        InjectChallengeTestFlag message,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.Id == message.RuntimeInstanceId
                && runtime.ChallengeId == message.ChallengeId
                && runtime.Purpose == RuntimePurpose.TemplateTest
                && runtime.TestFlagDelivery == RuntimeTestFlagDelivery.Command
                && runtime.TestFlagState == RuntimeTestFlagState.Pending
                && runtime.State == RuntimeState.Running
                && runtime.RunnerId == message.RunnerId
                && runtime.ProviderReceipt != null)
            .Join(
                db.Challenges.AsNoTracking(),
                runtime => runtime.ChallengeId,
                challenge => challenge.Id,
                (runtime, challenge) => new { Runtime = runtime, Challenge = challenge })
            .Join(
                db.ChallengeFlags.AsNoTracking(),
                target => target.Runtime.Id,
                flag => flag.SpecificationId,
                (target, flag) => new { target.Runtime, target.Challenge, Flag = flag })
            .Where(target => target.Flag.Id == message.ChallengeFlagId
                && target.Flag.ChallengeId == message.ChallengeId
                && target.Flag.SpecificationKind == SpecificationKind.RuntimeInstance
                && target.Flag.DeletedAt == null
                && target.Flag.ValidUntil == null)
            .Select(target => new
            {
                target.Runtime.RuntimeKind,
                target.Runtime.RuntimeProvider,
                ProviderReceipt = target.Runtime.ProviderReceipt!,
                target.Flag.Flag,
                target.Challenge.Definition
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
            message.RuntimeInstanceId,
            target.RuntimeKind,
            target.RuntimeProvider,
            target.ProviderReceipt.ToData(),
            target.Flag,
            injection.Command,
            injection.ServiceName,
            TimeSpan.FromSeconds(injection.TimeoutSeconds));
    }

    public async Task CompleteAsync(
        InjectChallengeTestFlag message,
        RuntimeTestFlagState state,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId
                && candidate.ChallengeId == message.ChallengeId
                && candidate.Purpose == RuntimePurpose.TemplateTest
                && candidate.TestFlagState == RuntimeTestFlagState.Pending
                && candidate.RunnerId == message.RunnerId,
            cancellationToken);
        if (runtime is null)
            return;

        runtime.TestFlagState = state;
        var flag = await db.ChallengeFlags.SingleOrDefaultAsync(
            candidate => candidate.Id == message.ChallengeFlagId
                && candidate.ChallengeId == message.ChallengeId
                && candidate.SpecificationKind == SpecificationKind.RuntimeInstance
                && candidate.SpecificationId == message.RuntimeInstanceId,
            cancellationToken);
        if (flag is not null)
        {
            if (state == RuntimeTestFlagState.Succeeded)
                flag.ValidStart ??= timeProvider.GetUtcNow();
            else
                flag.ValidUntil ??= timeProvider.GetUtcNow();
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

[NonTransactional]
public sealed class ChallengeTestFlagInjectionHandler(
    IChallengeTestFlagInjectionStore store,
    IAwdFlagInjectionExecutor executor,
    IPostCommitMessagePublisher outbox,
    IOptions<RunnerOptions> runnerOptions,
    TimeProvider timeProvider)
{
    public async Task<MessageExecutionOutcome> Handle(
        InjectChallengeTestFlag message,
        CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            runnerOptions.Value.Pool,
            runnerOptions.Value.Id);
        var work = await store.ReadAsync(message, cancellationToken);
        if (work is null)
        {
            await store.CompleteAsync(message, RuntimeTestFlagState.Failed, cancellationToken);
            return MessageExecutionOutcome.Superseded;
        }

        try
        {
            var result = await executor.ExecuteAsync(work, cancellationToken);
            if (!result.TimedOut && result.ExitCode == 0)
            {
                await store.CompleteAsync(message, RuntimeTestFlagState.Succeeded, cancellationToken);
                return MessageExecutionOutcome.Applied;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Retry below. Provider details are intentionally not persisted with protected Flag data.
        }

        if (message.FailedAttempts < 2)
        {
            var retryAt = timeProvider.GetUtcNow().AddSeconds(2 << message.FailedAttempts);
            await outbox.ScheduleToRunnerNodeAsync(
                message with { FailedAttempts = message.FailedAttempts + 1 },
                retryAt);
            await outbox.FlushOutgoingMessagesAsync();
            return MessageExecutionOutcome.DeferredSchedule;
        }

        await store.CompleteAsync(message, RuntimeTestFlagState.Failed, cancellationToken);
        return MessageExecutionOutcome.Applied;
    }
}
