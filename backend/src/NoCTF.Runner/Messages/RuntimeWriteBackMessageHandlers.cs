using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeProvisionWriteBackMessageHandler(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events,
    TimeProvider timeProvider)
{
    public Task Handle(RuntimeProvisioned message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ProvisionedAsync(
            message, db, outbox, events, timeProvider, cancellationToken);

    public Task Handle(RuntimeProvisionFailed message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ProvisionFailedAsync(
            message, db, events, timeProvider, cancellationToken);

    public Task Handle(RuntimeProvisionTerminated message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ProvisionTerminatedAsync(
            message, db, outbox, events, timeProvider, cancellationToken);

    public Task Handle(RuntimeProvisionCanceled message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.ProvisionCanceledAsync(
            message, db, outbox, events, timeProvider, cancellationToken);
}

public sealed class RuntimeStopWriteBackMessageHandler(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
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
            message, db, events, cancellationToken);

    public Task Handle(RuntimeStopFailed message, CancellationToken cancellationToken) =>
        RuntimeWriteBackOperations.StopFailedAsync(
            message, db, events, timeProvider, cancellationToken);
}

internal static class RuntimeWriteBackHandler
{
    public static Task Handle(
        RuntimeProvisioned message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
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
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);

    public static Task Handle(
        RuntimeProvisionTerminated message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
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
        ITransactionalMessageOutbox outbox,
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
        ITransactionalMessageOutbox outbox,
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
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.ForceTerminationFailedAsync(
            message,
            db,
            events ?? NullCompetitionEventRecorder.Instance,
            cancellationToken);

    public static Task Handle(
        RuntimeStopFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null) =>
        RuntimeWriteBackOperations.StopFailedAsync(
            message,
            db,
            events ?? NullCompetitionEventRecorder.Instance,
            TimeProvider.System,
            cancellationToken);

    public static Task Handle(
        RuntimeProvisionCanceled message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
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
