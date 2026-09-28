using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Storage;

public sealed class ManagedFileUploadRegistry(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ILogger<ManagedFileUploadRegistry> logger) : IManagedFileUploadRegistry
{
    private static readonly TimeSpan CleanupLease = TimeSpan.FromHours(24);

    public async Task RegisterAsync(
        ManagedFileUpload file,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Files.Add(new StoredFile
        {
            Id = file.FileId,
            ObjectKey = file.ObjectKey,
            FileName = file.FileName,
            ContentType = file.ContentType,
            ByteLength = file.ByteLength,
            Sha256 = Convert.FromHexString(file.Sha256),
            CreatedAt = createdAt
        });
        await outbox.ScheduleAsync(new CleanupFile(file.FileId), createdAt.Add(CleanupLease));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
    }

    public async Task AbandonAsync(Guid fileId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Files.AsNoTracking().AnyAsync(
                candidate => candidate.Id == fileId,
                cancellationToken))
            return;
        await outbox.PublishAsync(new CleanupFile(fileId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await FlushAsync(fileId);
    }

    private async Task FlushAsync(Guid fileId)
    {
        try
        {
            await outbox.FlushCommittedMessagesAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "File {FileId} cleanup message was committed but not flushed immediately.",
                fileId);
        }
    }
}

public sealed class FileReferenceLock
{
    public async Task<bool> AcquireAsync(
        NoCtfDbContext db,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        _ = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A transaction is required to protect a file reference.");
        return await db.Files.AsNoTracking().AnyAsync(
            file => file.Id == fileId,
            cancellationToken);
    }
}
