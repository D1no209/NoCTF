using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeProvisionWriteBackMessageHandler(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder events,
    TimeProvider timeProvider,
    IRunnerCapacityGate capacity)
{
    public async Task Handle(RuntimeProvisioned message, CancellationToken cancellationToken)
    {
        await RuntimeWriteBackOperations.ProvisionedAsync(
            message, db, outbox, events, timeProvider, cancellationToken);
        if (await db.RuntimeInstances.AsNoTracking().AnyAsync(runtime => runtime.Id == message.RuntimeInstanceId
                && runtime.RunnerId == message.RunnerId && runtime.State == RuntimeState.Running, cancellationToken))
            await capacity.CompleteStartupAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken);
    }

    public Task Handle(RuntimeProvisionFailed message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ProvisionFailedAsync(
            message, db, outbox, events, timeProvider, cancellationToken);

    public Task Handle(RuntimeProvisionTerminated message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ProvisionTerminatedAsync(
            message, db, outbox, events, timeProvider, cancellationToken);

    public Task Handle(RuntimeProvisionCanceled message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ProvisionCanceledAsync(
            message, db, outbox, events, timeProvider, cancellationToken);
}

public sealed class RuntimeStopWriteBackMessageHandler(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder events,
    TimeProvider timeProvider)
{
    public Task Handle(RuntimeStopped message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.StoppedAsync(
            message, db, outbox, events, timeProvider, cancellationToken);

    public Task Handle(RuntimeForceTerminated message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ForceTerminatedAsync(
            message, db, outbox, events, timeProvider, cancellationToken);

    public Task Handle(
        RuntimeForceTerminationFailed message,
        CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ForceTerminationFailedAsync(
            message, db, outbox, events, cancellationToken);

    public Task Handle(RuntimeStopFailed message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.StopFailedAsync(
            message, db, outbox, events, timeProvider, cancellationToken);
}

internal static class RuntimeWriteBackHandler
{
    public static Task Handle(
        RuntimeProvisioned message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.ProvisionedAsync(
            message,
            db,
            outbox,
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);

    public static Task Handle(
        RuntimeProvisionFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.ProvisionFailedAsync(
            message,
            db,
            null,
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);

    public static Task Handle(
        RuntimeProvisionTerminated message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.ProvisionTerminatedAsync(
            message,
            db,
            outbox,
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);

    public static Task Handle(
        RuntimeStopped message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.StoppedAsync(
            message,
            db,
            outbox,
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);

    public static Task Handle(
        RuntimeForceTerminated message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.ForceTerminatedAsync(
            message,
            db,
            outbox,
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);

    public static Task Handle(
        RuntimeForceTerminationFailed message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.ForceTerminationFailedAsync(
            message,
            db,
            outbox,
            events ?? NullCompetitionEventRecorder.Instance,
            cancellationToken);

    public static Task Handle(
        RuntimeStopFailed message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.StopFailedAsync(
            message,
            db,
            outbox,
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);

    public static Task Handle(
        RuntimeProvisionCanceled message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.ProvisionCanceledAsync(
            message,
            db,
            outbox,
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);
}
