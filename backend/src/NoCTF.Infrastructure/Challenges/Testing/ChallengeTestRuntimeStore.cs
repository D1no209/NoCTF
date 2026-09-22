using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Commands.Idempotency;

namespace NoCTF.Infrastructure.Challenges.Testing;

public sealed class ChallengeTestRuntimeStore(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placementPolicy,
    ITransactionalMessageOutbox outbox,
    IRequestReplay? replay = null) : IChallengeTestRuntimeStore
{
    public async Task<ChallengeTestRuntimeView?> FindAsync(
        Guid challengeId,
        Guid actorUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(challengeId, actorUserId, isAdministrator, cancellationToken))
            return null;

        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(instance =>
                instance.ChallengeId == challengeId
                && instance.Purpose == RuntimePurpose.TemplateTest)
            .OrderByDescending(instance => instance.CreatedAt)
            .ThenByDescending(instance => instance.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return runtime is null
            ? null
            : await MapAsync(runtime, cancellationToken);
    }

    public async Task<ChallengeTestRuntimeResult> MutateAsync(
        ChallengeTestRuntimeCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var challenge = await ChallengeTemplateCriticalSection.AcquireAsync(
            db,
            command.ChallengeId,
            cancellationToken);
        if (challenge is null
            || challenge.DeletedAt is not null
            || !CanManage(challenge, command.ActorUserId, command.IsAdministrator))
        {
            return new(null, RuntimeMutationFailure.NotFound);
        }

        var prior = replay is null ? null : await replay.FindAsync<RuntimeCommandReceipt>(
            new(command.ActorUserId, ReplayOperation.TemplateTestRuntimeMutation, Guid.Empty, command.ChallengeId),
            new { command.Action, command.Extension }, cancellationToken);
        if (prior is not null)
        {
            var original = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(item => item.Id == prior.RuntimeInstanceId, cancellationToken);
            return original is null ? new(null, RuntimeMutationFailure.NotFound) : new(await MapAsync(original, cancellationToken));
        }
        var current = await db.RuntimeInstances
            .Where(instance =>
                instance.ChallengeId == command.ChallengeId
                && instance.Purpose == RuntimePurpose.TemplateTest)
            .OrderByDescending(instance => instance.CreatedAt)
            .ThenByDescending(instance => instance.Id)
            .FirstOrDefaultAsync(cancellationToken);

        ChallengeRuntimeTemplate? template = null;
        Guid? newRuntimeInstanceId = null;
        ChallengeTestFlagPlan? newFlagPlan = null;
        if (command.Action is RuntimeAction.Start or RuntimeAction.Reset)
        {
            try
            {
                template = templates.Get(challenge.Mode, challenge.DefinitionJson);
                if (template is null
                    || template.RuntimeKind is not RuntimeKind.Container and not RuntimeKind.Compose)
                {
                    return new(null, RuntimeMutationFailure.Unsupported);
                }
                newRuntimeInstanceId = Guid.CreateVersion7(command.Now);
                newFlagPlan = ChallengeTestFlagFactory.Create(
                    challenge.Mode,
                    challenge.DefinitionJson,
                    template,
                    challenge.Id,
                    newRuntimeInstanceId.Value);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or GameModeConfigurationException
                or FormatException
                or ArgumentException
                or System.Text.Json.JsonException)
            {
                return new(null, RuntimeMutationFailure.ConfigurationInvalid);
            }
        }

        RuntimeInstance entity;
        switch (command.Action)
        {
            case RuntimeAction.Start:
                if (current?.State == RuntimeState.Stopping)
                    return new(null, RuntimeMutationFailure.InvalidState);
                if (current is not null && IsRunningOrStarting(current.State))
                    return new(await MapAsync(current, cancellationToken));
                if (await HasStoppingRuntimeAsync(command.ChallengeId, cancellationToken))
                    return new(null, RuntimeMutationFailure.InvalidState);
                await QueueFailedCleanupAsync(command.ChallengeId, command.Now, cancellationToken);
                entity = await CreateAsync(
                    challenge, template!, newRuntimeInstanceId!.Value, newFlagPlan!, command.Now);
                break;
            case RuntimeAction.Reset:
                if (current is null || !IsRunningOrStarting(current.State))
                    return new(null, RuntimeMutationFailure.InvalidState);
                await StopAsync(current, command.Now, cancellationToken);
                entity = await CreateAsync(
                    challenge, template!, newRuntimeInstanceId!.Value, newFlagPlan!, command.Now);
                break;
            case RuntimeAction.Stop:
                if (current is null
                    || current.State is RuntimeState.Stopped or RuntimeState.Failed)
                {
                    return new(null, RuntimeMutationFailure.InvalidState);
                }
                if (current.State == RuntimeState.Stopping)
                    return new(await MapAsync(current, cancellationToken));
                entity = current;
                await StopAsync(current, command.Now, cancellationToken);
                break;
            case RuntimeAction.Extend:
                if (current is null
                    || current.State != RuntimeState.Running
                    || current.ExpiresAt is null
                    || command.Extension is null)
                {
                    return new(null, RuntimeMutationFailure.InvalidState);
                }
                var remaining = current.ExpiresAt.Value - command.Now;
                if (remaining <= TimeSpan.Zero || remaining >= TimeSpan.FromMinutes(10))
                    return new(null, RuntimeMutationFailure.InvalidState);
                current.ExpiresAt = command.Now.Add(command.Extension.Value);
                entity = current;
                break;
            default:
                return new(null, RuntimeMutationFailure.Unsupported);
        }

        try
        {
            replay?.Store(new RuntimeCommandReceipt(entity.Id));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushCommittedMessagesAsync();
            return new(await MapAsync(entity, cancellationToken));
        }
        catch (DbUpdateException exception) when (!TransactionFailureClassifier.IsRetryable(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new(null, RuntimeMutationFailure.Conflict);
        }
    }

    private async Task<RuntimeInstance> CreateAsync(
        Challenge challenge,
        ChallengeRuntimeTemplate template,
        Guid runtimeInstanceId,
        ChallengeTestFlagPlan flagPlan,
        DateTimeOffset now)
    {
        if (flagPlan.Flag is { } flag)
        {
            db.ChallengeFlags.Add(new ChallengeFlag
            {
                Id = Guid.CreateVersion7(now),
                ChallengeId = challenge.Id,
                Flag = flag,
                FlagSha256 = ManageChallengeFlags.Hash(flag),
                MatchKind = ChallengeFlagMatchKind.Exact,
                SpecificationKind = SpecificationKind.RuntimeInstance,
                SpecificationId = runtimeInstanceId,
                CreatedAt = now
            });
        }

        var placement = placementPolicy.Resolve(template.RuntimeKind);
        var entity = new RuntimeInstance
        {
            Id = runtimeInstanceId,
            ChallengeId = challenge.Id,
            Purpose = RuntimePurpose.TemplateTest,
            AccessMode = RuntimeAccessMode.DirectAndWsrx,
            TestFlagDelivery = flagPlan.Delivery,
            TestFlagState = flagPlan.InitialState,
            RuntimeKind = template.RuntimeKind,
            RuntimeProvider = placement.Provider,
            State = RuntimeState.Queued,
            CreatedAt = now
        };
        db.RuntimeInstances.Add(entity);
        await outbox.PublishAsync(new DispatchRuntime(entity.Id));
        return entity;
    }

    private async Task StopAsync(
        RuntimeInstance runtime,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EndPendingFlagAsync(runtime, RuntimeTestFlagState.Canceled, now, cancellationToken);
        if (runtime.State == RuntimeState.Queued
            && runtime.RunnerId is null
            && string.IsNullOrWhiteSpace(runtime.ProviderReceiptJson))
        {
            runtime.State = RuntimeState.Stopped;
            runtime.StoppedAt = now;
            return;
        }

        runtime.State = RuntimeState.Stopping;
        runtime.FailureCode = null;
        await outbox.PublishAsync(new StopRuntime(runtime.Id));
    }

    private async Task QueueFailedCleanupAsync(
        Guid challengeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var failed = await db.RuntimeInstances
            .Where(instance =>
                instance.ChallengeId == challengeId
                && instance.Purpose == RuntimePurpose.TemplateTest
                && instance.State == RuntimeState.Failed
                && instance.RunnerId != null)
            .OrderByDescending(instance => instance.CreatedAt)
            .ThenByDescending(instance => instance.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (failed is null)
            return;

        failed.State = RuntimeState.Stopping;
        failed.FailureCode = null;
        await EndPendingFlagAsync(failed, RuntimeTestFlagState.Failed, now, cancellationToken);
        await outbox.PublishAsync(new StopRuntime(failed.Id));
    }

    private async Task EndPendingFlagAsync(
        RuntimeInstance runtime,
        RuntimeTestFlagState state,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (runtime.TestFlagDelivery == RuntimeTestFlagDelivery.NotRequired)
            return;
        if (runtime.TestFlagState == RuntimeTestFlagState.Pending)
            runtime.TestFlagState = state;
        var flag = await db.ChallengeFlags.SingleOrDefaultAsync(
            candidate => candidate.ChallengeId == runtime.ChallengeId
                && candidate.SpecificationKind == SpecificationKind.RuntimeInstance
                && candidate.SpecificationId == runtime.Id,
            cancellationToken);
        if (flag is not null)
            flag.ValidUntil ??= at;
    }

    private Task<bool> HasStoppingRuntimeAsync(
        Guid challengeId,
        CancellationToken cancellationToken) =>
        db.RuntimeInstances.AnyAsync(instance =>
            instance.ChallengeId == challengeId
            && instance.Purpose == RuntimePurpose.TemplateTest
            && instance.State == RuntimeState.Stopping,
            cancellationToken);

    private Task<bool> CanManageAsync(
        Guid challengeId,
        Guid actorUserId,
        bool isAdministrator,
        CancellationToken cancellationToken) =>
        db.Challenges.AsNoTracking().AnyAsync(challenge =>
            challenge.Id == challengeId
            && (isAdministrator
                || challenge.OwnerId == actorUserId
                || challenge.ManagerIds.Contains(actorUserId)),
            cancellationToken);

    private static bool CanManage(
        Challenge challenge,
        Guid actorUserId,
        bool isAdministrator) =>
        isAdministrator
        || challenge.OwnerId == actorUserId
        || challenge.ManagerIds.Contains(actorUserId);

    private async Task<ChallengeTestRuntimeView> MapAsync(
        RuntimeInstance runtime,
        CancellationToken cancellationToken)
    {
        var testFlag = runtime.TestFlagDelivery == RuntimeTestFlagDelivery.NotRequired
            ? null
            : await db.ChallengeFlags.AsNoTracking()
                .Where(flag =>
                    flag.ChallengeId == runtime.ChallengeId
                    && flag.SpecificationKind == SpecificationKind.RuntimeInstance
                    && flag.SpecificationId == runtime.Id)
                .Select(flag => flag.Flag)
                .SingleOrDefaultAsync(cancellationToken);
        return new(
            runtime.Id,
            runtime.ChallengeId
                ?? throw new InvalidOperationException("Template test Runtime has no ChallengeId."),
            runtime.RuntimeKind,
            runtime.RuntimeProvider,
            runtime.State,
            runtime.FailureCode,
            runtime.TestFlagDelivery ?? RuntimeTestFlagDelivery.NotRequired,
            runtime.TestFlagState ?? RuntimeTestFlagState.NotRequired,
            testFlag,
            runtime.CreatedAt,
            runtime.RunningAt,
            runtime.ExpiresAt,
            runtime.StoppedAt,
            runtime.AccessMode,
            runtime.AccessEndpoints.OrderBy(endpoint => endpoint.BindingIndex)
                .Select(endpoint => new RuntimeAccessEndpointView(
                    endpoint.BindingIndex,
                    endpoint.DirectAddress,
                    endpoint.TargetHost,
                    endpoint.TargetPort)).ToArray());
    }

    private static bool IsRunningOrStarting(RuntimeState state) =>
        state is RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running;
}
