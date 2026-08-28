using FluentStorage.Storage;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Persistence;
using CompetitionLifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase;

namespace NoCTF.Worker;

public sealed class FileCleanupMessageHandler(NoCtfDbContext db, IStore objects)
{
    public Task Handle(CleanupFile message, CancellationToken cancellationToken) =>
        BackendMessageOperations.CleanupFileAsync(message, db, objects, cancellationToken);
}

public sealed class AwdMessageHandler(
    NoCtfDbContext db,
    AwdCheckerConfigurationCatalog configurations,
    ITransactionalMessageOutbox outbox,
    IAwdRoundCoordinator rounds,
    IInternalResultStore results)
{
    public Task Handle(DispatchAwdCheckers message, CancellationToken cancellationToken) =>
        BackendMessageOperations.DispatchAwdCheckersAsync(
            message, db, configurations, outbox, cancellationToken);

    public Task Handle(AdvanceAwdRound message, CancellationToken cancellationToken) =>
        BackendMessageOperations.AdvanceAwdRoundAsync(message, rounds, cancellationToken);

    public Task Handle(GenerateAwdFlags message, CancellationToken cancellationToken) =>
        BackendMessageOperations.GenerateAwdFlagsAsync(message, rounds, cancellationToken);

    public Task Handle(AwdFlagInjectionFailed message, CancellationToken cancellationToken) =>
        BackendMessageOperations.AwdFlagInjectionFailedAsync(message, db, cancellationToken);

    public Task Handle(AwdCheckerCallbackMissing message, CancellationToken cancellationToken) =>
        BackendMessageOperations.AwdCheckerCallbackMissingAsync(
            message, results, db, cancellationToken);
}

public sealed class CompetitionLifecycleMessageHandler(
    CompetitionLifecycleAdvancer advancer,
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox)
{
    public Task Handle(
        AdvanceCompetitionLifecycle message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.AdvanceCompetitionLifecycleAsync(
            message, advancer, db, outbox, cancellationToken);

    public Task Handle(
        ApplyCompetitionVisibility message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.ApplyCompetitionVisibilityAsync(message, cancellationToken);
}

public sealed class AwdpMessageHandler(
    IInternalResultStore results,
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events,
    TimeProvider timeProvider)
{
    public Task Handle(AwdpFixResult message, CancellationToken cancellationToken) =>
        BackendMessageOperations.RecordAwdpFixResultAsync(message, results, cancellationToken);

    public Task Handle(
        CompleteAwdpFixRecovery message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.CompleteAwdpFixRecoveryAsync(
            message, db, outbox, cancellationToken, events);

    public Task Handle(
        ExpireAwdpFixVerification message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.ExpireAwdpFixVerificationAsync(
            message, db, outbox, timeProvider, cancellationToken, events);
}

public sealed class RuntimeDispatchMessageHandler(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placement,
    IRunnerCapacityGate capacity,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events,
    IAwdRuntimeProvisioner awdRuntimes,
    IKohRuntimeProvisioner kohRuntimes,
    TimeProvider timeProvider)
{
    public Task Handle(DispatchRuntime message, CancellationToken cancellationToken) =>
        BackendMessageOperations.DispatchRuntimeAsync(
            message, db, templates, placement, capacity, outbox, timeProvider, cancellationToken, events);

    public Task Handle(StopRuntime message, CancellationToken cancellationToken) =>
        BackendMessageOperations.StopRuntimeAsync(
            message, db, outbox, timeProvider, cancellationToken, events);

    public Task Handle(
        CleanupCompetitionRuntimes message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.CleanupCompetitionRuntimesAsync(
            message, db, outbox, timeProvider, cancellationToken, events);

    public Task Handle(
        ProvisionCompetitionRuntimes message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.ProvisionCompetitionRuntimesAsync(
            message, awdRuntimes, kohRuntimes, db, outbox, timeProvider, cancellationToken);

    public Task Handle(
        ReconcileRunnerAssignments message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.ReconcileRunnerAssignmentsAsync(
            message, db, capacity, placement, outbox, timeProvider, cancellationToken);
}

public sealed class GameplayFactDrainMessageHandler(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider)
{
    public Task Handle(
        DrainGameplayFactEvaluation message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.DrainGameplayFactEvaluationAsync(
            message, db, outbox, timeProvider, cancellationToken);

    public Task Handle(
        DrainGameplayFactRejudge message,
        CancellationToken cancellationToken) =>
        BackendMessageOperations.DrainGameplayFactRejudgeAsync(
            message, db, outbox, timeProvider, cancellationToken);
}
