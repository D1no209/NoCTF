using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Storage;

public sealed class ManagedFileUploadRegistry(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ILogger<ManagedFileUploadRegistry> logger) : IManagedFileUploadRegistry
{
    private static readonly TimeSpan CleanupLease = TimeSpan.FromHours(24);

    public async Task RegisterAsync(
        Guid fileId,
        StoredObject metadata,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Files.Add(new StoredFile
        {
            Id = fileId,
            ObjectKey = metadata.ObjectKey,
            FileName = metadata.FileName,
            ContentType = metadata.ContentType,
            ByteLength = metadata.Length,
            Sha256 = Convert.FromHexString(metadata.Sha256),
            CreatedAt = createdAt
        });
        await outbox.ScheduleAsync(new CleanupFile(fileId), createdAt.Add(CleanupLease));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await FlushAsync(fileId);
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
            await outbox.FlushOutgoingMessagesAsync();
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
        CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM files WHERE id = {fileId} FOR UPDATE",
            cancellationToken) == 1;
}
