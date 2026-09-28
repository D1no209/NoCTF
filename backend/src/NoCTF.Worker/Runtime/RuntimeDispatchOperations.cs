using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Platform;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Domain.Notifications;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;
using NoCTF.GameModes.PatchVerification.Configuration;
using NoCTF.GameModes.PatchVerification.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Worker.Runtime;
using CompetitionLifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Observability;
using System.Diagnostics;

namespace NoCTF.Worker;

internal static partial class BackendMessageOperations
{
    public static async Task DispatchRuntimeAsync(
        DispatchRuntime message,
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        IRuntimePlacementPolicy placementPolicy,
        IRunnerCapacityGate capacity,
        IPostCommitMessagePublisher outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null,
        RuntimeResourceBudgetPolicy? budgets = null)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
                    ? await db.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        cancellationToken) : null;
                await DispatchRuntimeCoreAsync(message, db, templates, placementPolicy, capacity, outbox,
                    timeProvider, cancellationToken, events, budgets ?? new());
                if (transaction is not null)
                {
                    var commitStarted = Stopwatch.GetTimestamp();
                    await transaction.CommitAsync(cancellationToken);
                    NoCtfTelemetry.RecordRuntimeDispatchStage(
                        RuntimeDispatchPerformanceStage.TransactionCommit,
                        Stopwatch.GetElapsedTime(commitStarted).TotalSeconds);
                    var publishStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        await outbox.FlushCommittedMessagesAsync();
                    }
                    finally
                    {
                        NoCtfTelemetry.RecordRuntimeDispatchStage(
                            RuntimeDispatchPerformanceStage.PostCommitPublish,
                            Stopwatch.GetElapsedTime(publishStarted).TotalSeconds);
                    }
                }
                return;
            }
            catch (Exception exception) when (attempt < 2
                && TransactionFailureClassifier.IsRetryable(exception))
            {
                outbox.DiscardPendingMessages();
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(
                    Random.Shared.Next(25, 76) * (attempt + 1)), cancellationToken);
            }
        }
        throw new InvalidOperationException("Runtime dispatch retry loop did not complete.");
    }

    private static async Task DispatchRuntimeCoreAsync(
        DispatchRuntime message, NoCtfDbContext db, IChallengeRuntimeTemplateCatalog templates,
        IRuntimePlacementPolicy placementPolicy, IRunnerCapacityGate capacity,
        IPostCommitMessagePublisher outbox, TimeProvider timeProvider, CancellationToken cancellationToken,
        ICompetitionEventRecorder? events, RuntimeResourceBudgetPolicy budgets)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var targetReadStarted = Stopwatch.GetTimestamp();
        var runtimeScope = await db.RuntimeInstances.AsNoTracking()
            .Where(candidate => candidate.Id == message.RuntimeInstanceId)
            .Select(candidate => new { candidate.Purpose, candidate.ChallengeId })
            .SingleOrDefaultAsync(cancellationToken);
        if (runtimeScope is null)
            return;
        if (runtimeScope.Purpose == RuntimePurpose.TemplateTest)
        {
            if (runtimeScope.ChallengeId is not Guid challengeId
                || await ChallengeTemplateCriticalSection.AcquireAsync(
                    db,
                    challengeId,
                    cancellationToken) is null)
            {
                return;
            }
        }

        var instance = await db.RuntimeInstances.AsSplitQuery().SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null || instance.State is not (RuntimeState.Queued or RuntimeState.Provisioning))
            return;

        var target = await ResolveRuntimeDispatchTargetAsync(instance, db, cancellationToken);
        NoCtfTelemetry.RecordRuntimeDispatchStage(
            RuntimeDispatchPerformanceStage.TargetRead,
            Stopwatch.GetElapsedTime(targetReadStarted).TotalSeconds);
        if (target is null)
        {
            await RejectInvalidConfigurationAsync(
                instance, db, outbox, events, timeProvider, cancellationToken);
            return;
        }

        var definitionStarted = Stopwatch.GetTimestamp();
        ChallengeRuntimeTemplate? template;
        try
        {
            var patchVerificationConfiguration = IsPatchVerificationTarget(instance.Purpose)
                ? PatchVerificationConfigurationResolver.Resolve(
                    target.Mode,
                    target.CompetitionConfiguration!,
                    target.Rules!,
                    target.Definition!)
                : null;
            template = patchVerificationConfiguration?.Runtime
                ?? templates.Get(target.Definition);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or GameModeConfigurationException
            or JsonException)
        {
            template = null;
        }
        if (template is null)
        {
            await RejectInvalidConfigurationAsync(
                instance, db, outbox, events, timeProvider, cancellationToken);
            return;
        }

        string? perTeamFlag = null;
        if (instance.Purpose == RuntimePurpose.TemplateTest
            && instance.TestFlagDelivery == RuntimeTestFlagDelivery.Environment)
        {
            perTeamFlag = await db.ChallengeFlags.AsNoTracking()
                .Where(flag =>
                    flag.ChallengeId == target.ChallengeId
                    && flag.SpecificationKind == SpecificationKind.RuntimeInstance
                    && flag.SpecificationId == instance.Id
                    && flag.DeletedAt == null
                    && flag.ValidUntil == null)
                .Select(flag => flag.Flag)
                .SingleOrDefaultAsync(cancellationToken);
        }
        else if (target.Mode is GameMode.Ctf or GameMode.Awdp
            && !IsPatchVerificationTarget(instance.Purpose)
            && template.FlagSource == RuntimeFlagSource.PerTeam)
        {
            if (instance.TeamId is Guid teamId
                && instance.CompetitionChallengeId is Guid competitionChallengeId)
            {
                var specificationKind = target.Mode == GameMode.Awdp
                    ? SpecificationKind.RuntimeInstance
                    : SpecificationKind.RuntimeDefinition;
                var specificationId = target.Mode == GameMode.Awdp
                    ? instance.Id
                    : competitionChallengeId;
                perTeamFlag = await db.ChallengeFlags.AsNoTracking()
                    .Where(flag =>
                        flag.CompetitionChallengeId == competitionChallengeId
                        && flag.TeamId == teamId
                        && flag.SpecificationKind == specificationKind
                        && flag.SpecificationId == specificationId
                        && flag.DeletedAt == null
                        && flag.ValidUntil == null)
                    .Select(flag => flag.Flag)
                    .SingleOrDefaultAsync(cancellationToken);
            }
        }
        if ((instance.TestFlagDelivery == RuntimeTestFlagDelivery.Environment
                || target.Mode is GameMode.Ctf or GameMode.Awdp
                    && !IsPatchVerificationTarget(instance.Purpose)
                    && template.FlagSource == RuntimeFlagSource.PerTeam)
            && perTeamFlag is null)
        {
            await RejectInvalidConfigurationAsync(
                instance, db, outbox, events, timeProvider, cancellationToken);
            return;
        }

        var placement = placementPolicy.Resolve(instance.RuntimeKind);
        if (placement.Provider != instance.RuntimeProvider)
        {
            await RejectInvalidConfigurationAsync(
                instance, db, outbox, events, timeProvider, cancellationToken);
            return;
        }

        var limits = template.Limits
            ?? new RuntimeResourceLimits(512 * 1024 * 1024, 500_000_000, 256);
        RuntimeResourceLimits budget;
        try
        {
            if (template.Definition is ComposeRuntimeDefinition compose)
            {
                var pids = limits.PidsLimit;
                limits = RuntimeResourceBudgetPolicy.Sum(compose.ServiceResources.Values
                    .Select(value => budgets.EffectiveLimit(value, instance.RuntimeProvider)), pids);
                budget = RuntimeResourceBudgetPolicy.Sum(budgets.ForCompose(compose.ServiceResources, instance.RuntimeProvider).Values, pids);
            }
            else
            {
                limits = budgets.EffectiveLimit(limits, instance.RuntimeProvider);
                budget = budgets.Calculate(limits, instance.RuntimeProvider);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            await RejectInvalidConfigurationAsync(instance, db, outbox, events, timeProvider, cancellationToken);
            return;
        }
        var priorAllocation = instance.CapacityAllocations.Items.SingleOrDefault(item => !item.Identity.IsAuxiliary);
        if (priorAllocation is not null && priorAllocation.Limit != RuntimeResourceBudgetPolicy.ToAmount(limits))
        {
            // A previous creation may have succeeded without writing its receipt. Keep the
            // allocation until Runner cleanup proves absence; never reprice or release here.
            instance.RunnerId = priorAllocation.RunnerId;
            await RejectInvalidConfigurationAsync(instance, db, outbox, events, timeProvider, cancellationToken);
            return;
        }
        NoCtfTelemetry.RecordRuntimeDispatchStage(
            RuntimeDispatchPerformanceStage.DefinitionPreparation,
            Stopwatch.GetElapsedTime(definitionStarted).TotalSeconds);
        var claimStarted = Stopwatch.GetTimestamp();
        var capacityClaim = await capacity.TryClaimAsync(new RunnerCapacityRequest(
            instance.Id,
            placement.RunnerPool,
            budget.MemoryBytes,
            budget.NanoCpus,
            budget.PidsLimit,
            Limit: RuntimeResourceBudgetPolicy.ToAmount(limits)), cancellationToken);
        NoCtfTelemetry.RecordRuntimeDispatchStage(
            RuntimeDispatchPerformanceStage.CapacityClaim,
            Stopwatch.GetElapsedTime(claimStarted).TotalSeconds);
        if (capacityClaim.Availability != RunnerCapacityAvailability.Claimed
            || string.IsNullOrWhiteSpace(capacityClaim.RunnerId))
        {
            // The cluster Singular Agent rebuilds work from Queued facts. A missing
            // or failed delayed delivery must never strand a capacity-blocked Runtime.
            await capacity.RecordWaitingAsync(instance.Id, capacityClaim.Failure ?? RunnerAdmissionFailure.NoEligibleRunner, cancellationToken);
            return;
        }
        await capacity.RecordWaitingAsync(instance.Id, null, cancellationToken);

        var persistenceStarted = Stopwatch.GetTimestamp();
        var runnerId = capacityClaim.RunnerId;
        IRuntimeProvisionMessage provision;
        try
        {
            if (IsPatchVerificationTarget(instance.Purpose))
            {
                var definition = PatchVerificationTargetDefinitionFactory.Create(
                    instance.Id,
                    template,
                    instance.RuntimeProvider,
                    instance.Purpose);
                if (instance.RuntimeProvider == RuntimeProvider.Docker)
                {
                    definition = definition with
                    {
                        PortMappings = definition.PortMappings.Keys.ToDictionary(
                            port => port,
                            _ => 0)
                    };
                }
                provision = new ProvisionContainerRuntime(
                    instance.Id,
                    runnerId,
                    definition);
            }
            else
            {
                provision = RuntimeClaimFactory.Create(
                    instance,
                    runnerId,
                    target.Mode,
                    template,
                    target.Definition,
                    perTeamFlag);
            }
            var stored = await db.RuntimeInstances.AsNoTracking().Where(runtime => runtime.Id == instance.Id)
                .Select(runtime => runtime.CapacityAllocations).SingleAsync(cancellationToken);
            var allocation = stored.Items.SingleOrDefault(item => !item.Identity.IsAuxiliary) ?? new RuntimeCapacityAllocation(
                NoCTF.Infrastructure.Runtime.Capacity.PersistedRunnerCapacityGate.PrimaryIdentity(instance), instance.GameplayFactId,
                runnerId, runnerId, RuntimeResourceBudgetPolicy.ToAmount(budget), RuntimeResourceBudgetPolicy.ToAmount(limits));
            var committed = allocation.Budget;
            provision = provision switch
            {
                ProvisionContainerRuntime container => container with
                {
                    Definition = container.Definition with
                    {
                        Limits = budgets.EffectiveLimit(container.Definition.Limits, instance.RuntimeProvider),
                        Budget = RuntimeResourceBudgetPolicy.ToLimits(committed)
                    }
                },
                ProvisionComposeRuntime compose => compose with
                {
                    Definition = compose.Definition with
                    {
                        ServiceBudgets = RuntimeResourceBudgetPolicy.RecreateComposeBudgets(
                            compose.Definition.ServiceResources, instance.RuntimeProvider, committed)
                    }
                },
                _ => provision
            };
            if (!RuntimeProvisionCapacity.Matches(allocation, provision))
                throw new InvalidOperationException("Provider request resources differ from the committed allocation.");
        }
        catch (InvalidOperationException)
        {
            // Cleanup owns release, including when a previous provider call has no receipt yet.
            instance.RunnerId = runnerId;
            await RejectInvalidConfigurationAsync(
                instance, db, outbox, events, timeProvider, cancellationToken);
            return;
        }

        instance.RunnerId = runnerId;
        if (!db.Database.IsRelational())
        {
            var amount = RuntimeResourceBudgetPolicy.ToAmount(limits);
            var allocationEntry = RuntimeCapacityAllocationEntry.FromValue(new(
                NoCTF.Infrastructure.Runtime.Capacity.PersistedRunnerCapacityGate.PrimaryIdentity(instance),
                instance.GameplayFactId, runnerId, runnerId,
                RuntimeResourceBudgetPolicy.ToAmount(budget), amount));
            instance.CapacityAllocationEntries.Add(allocationEntry);
            db.Entry(allocationEntry).State = EntityState.Added;
        }
        instance.State = RuntimeState.Provisioning;
        instance.FailureCode = null;
        await PublishRuntimeProvisionAsync(outbox, provision);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        NoCtfTelemetry.RecordRuntimeDispatchStage(
            RuntimeDispatchPerformanceStage.Persistence,
            Stopwatch.GetElapsedTime(persistenceStarted).TotalSeconds);
    }

    private static async Task FailAwdpSubmissionAsync(
        RuntimeInstance instance,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            timeProvider.GetUtcNow(),
            AwdpFixRuntimeCleanupMode.CallerManaged,
            cancellationToken);
    }

    private static async Task<RuntimeDispatchTarget?> ResolveRuntimeDispatchTargetAsync(
        RuntimeInstance instance,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        if (instance.Purpose == RuntimePurpose.TemplateTest)
        {
            if (instance.ChallengeId is not Guid challengeId)
                return null;
            var challenge = await db.Challenges.AsNoTracking()
                .AsSplitQuery()
                .SingleOrDefaultAsync(candidate => candidate.Id == challengeId, cancellationToken);
            return challenge is null
                ? null
                : new(
                    instance,
                    challenge.Id,
                    challenge.Mode,
                    challenge.Definition,
                    null,
                    null);
        }

        if (instance.CompetitionId is not Guid competitionId
            || instance.CompetitionChallengeId is not Guid competitionChallengeId)
            return null;
        var scope = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == competitionChallengeId
                && challenge.CompetitionId == competitionId)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.Challenge.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new
                {
                    ChallengeId = challenge.Id,
                    item.Competition.Mode,
                    challenge.Definition,
                    CompetitionConfiguration = item.Competition.ModeConfiguration,
                    Rules = item.Challenge.Rules
                })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        return scope is null
            ? null
            : new(
                instance,
                scope.ChallengeId,
                scope.Mode,
                scope.Definition,
                scope.CompetitionConfiguration,
                scope.Rules);
    }

    private static async Task RejectInvalidConfigurationAsync(
        RuntimeInstance instance,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var failedAt = timeProvider.GetUtcNow();
        instance.State = RuntimeState.Failed;
        instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
        if (instance.Purpose == RuntimePurpose.TemplateTest
            && instance.TestFlagState == RuntimeTestFlagState.Pending)
        {
            instance.TestFlagState = RuntimeTestFlagState.Failed;
            var flag = await db.ChallengeFlags.SingleOrDefaultAsync(
                candidate => candidate.ChallengeId == instance.ChallengeId
                    && candidate.SpecificationKind == SpecificationKind.RuntimeInstance
                    && candidate.SpecificationId == instance.Id,
                cancellationToken);
            if (flag is not null)
                flag.ValidUntil ??= failedAt;
        }
        await FailAwdpSubmissionAsync(
            instance, db, outbox, events, timeProvider, cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Error,
            failedAt,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private sealed record RuntimeDispatchTarget(
        RuntimeInstance Instance,
        Guid ChallengeId,
        GameMode Mode,
        ChallengeDefinition? Definition,
        CompetitionModeConfiguration? CompetitionConfiguration,
        CompetitionChallengeRules? Rules);

    private static bool IsPatchVerificationTarget(RuntimePurpose purpose) =>
        purpose is RuntimePurpose.AwdpTarget or RuntimePurpose.PatchVerificationTarget;

}
