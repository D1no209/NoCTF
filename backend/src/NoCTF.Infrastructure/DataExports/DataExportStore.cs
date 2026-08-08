using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.DataExports;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.DataExports;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.DataExports;

public sealed class DataExportStore(
    NoCtfDbContext db,
    IObjectStorage objectStorage,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider) : IDataExportStore
{
    private static readonly DataExportStatus[] ActiveStatuses =
        [DataExportStatus.Queued, DataExportStatus.Processing];

    public async Task<RequestDataExportResult> RequestAsync(
        RequestDataExportCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Scope == DataExportScope.PlatformAudit
            && !command.RequesterIsAdministrator)
        {
            return new(Failure: RequestDataExportFailure.Forbidden);
        }

        if (command.CompetitionId is Guid competitionId)
        {
            var competition = await db.Competitions.AsNoTracking()
                .Where(item => item.Id == competitionId)
                .Select(item => new { item.OwnerId, item.ManagerIds })
                .SingleOrDefaultAsync(cancellationToken);
            if (competition is null)
                return new(Failure: RequestDataExportFailure.SubjectNotFound);
            if (!command.RequesterIsAdministrator
                && competition.OwnerId != command.RequestedByUserId
                && !competition.ManagerIds.Contains(command.RequestedByUserId))
            {
                return new(Failure: RequestDataExportFailure.Forbidden);
            }
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var existing = await db.DataExports.AsNoTracking()
            .Where(item => item.RequestedByUserId == command.RequestedByUserId
                && item.Scope == command.Scope
                && ActiveStatuses.Contains(item.Status))
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return new(Map(existing), RequestDataExportFailure.ActiveExportExists);

        var entity = new DataExport
        {
            Id = Guid.CreateVersion7(now),
            Scope = command.Scope,
            CompetitionId = command.CompetitionId,
            RequestedByUserId = command.RequestedByUserId,
            RequestedAt = now,
            IncludeProtectedFlags = command.IncludeProtectedFlags,
            Reason = command.Reason,
            Status = DataExportStatus.Queued,
            ActiveSlot = 1,
            PurgeAt = now.AddDays(30)
        };
        db.DataExports.Add(entity);
        await outbox.PublishAsync(new GenerateDataExport(entity.Id));
        await outbox.ScheduleAsync(new PurgeDataExport(entity.Id), entity.PurgeAt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var concurrent = await db.DataExports.AsNoTracking()
                .Where(item => item.RequestedByUserId == command.RequestedByUserId
                    && item.Scope == command.Scope
                    && item.ActiveSlot == 1)
                .OrderByDescending(item => item.RequestedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (concurrent is null)
                throw;
            return new(Map(concurrent), RequestDataExportFailure.ActiveExportExists);
        }
        await outbox.FlushOutgoingMessagesAsync();
        return new(Map(entity));
    }

    public async Task<ListDataExportsResult> ListAsync(
        ListDataExportsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Scope == DataExportScope.PlatformAudit)
        {
            if (!query.RequesterIsAdministrator)
                return new(Failure: ListDataExportsFailure.Forbidden);
        }
        else if (query.CompetitionId is not Guid competitionId)
        {
            return new(Failure: ListDataExportsFailure.SubjectNotFound);
        }
        else
        {
            var competition = await db.Competitions.AsNoTracking()
                .Where(item => item.Id == competitionId)
                .Select(item => new { item.OwnerId, item.ManagerIds })
                .SingleOrDefaultAsync(cancellationToken);
            if (competition is null)
                return new(Failure: ListDataExportsFailure.SubjectNotFound);
            if (!query.RequesterIsAdministrator
                && competition.OwnerId != query.RequesterId
                && !competition.ManagerIds.Contains(query.RequesterId))
            {
                return new(Failure: ListDataExportsFailure.Forbidden);
            }
        }

        var items = await db.DataExports.AsNoTracking().Include(item => item.File)
            .Where(item => item.Scope == query.Scope
                && item.CompetitionId == query.CompetitionId
                && (query.RequesterIsAdministrator
                    || item.RequestedByUserId == query.RequesterId))
            .OrderByDescending(item => item.RequestedAt)
            .Take(50)
            .ToArrayAsync(cancellationToken);
        return new(items.Select(Map).ToArray());
    }

    public async Task<AccessDataExportResult> AccessAsync(
        AccessDataExportQuery query,
        CancellationToken cancellationToken)
    {
        var entity = await db.DataExports.AsNoTracking().Include(item => item.File)
            .SingleOrDefaultAsync(item => item.Id == query.DataExportId, cancellationToken);
        if (entity is null)
            return new(Failure: AccessDataExportFailure.NotFound);
        if (entity.Scope == DataExportScope.PlatformAudit
            && !query.RequesterIsAdministrator)
        {
            return new(Failure: AccessDataExportFailure.Forbidden);
        }
        if (!query.RequesterIsAdministrator)
        {
            if (entity.RequestedByUserId != query.RequesterId
                || entity.CompetitionId is not Guid competitionId)
            {
                return new(Failure: AccessDataExportFailure.Forbidden);
            }
            var stillAuthorized = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.Id == competitionId
                    && (item.OwnerId == query.RequesterId
                        || item.ManagerIds.Contains(query.RequesterId)),
                    cancellationToken);
            if (!stillAuthorized)
                return new(Failure: AccessDataExportFailure.Forbidden);
        }
        if (entity.IncludeProtectedFlags && !query.RequesterIsAdministrator)
            return new(Failure: AccessDataExportFailure.Forbidden);
        if (entity.Status == DataExportStatus.Expired
            || entity.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return new(Failure: AccessDataExportFailure.Expired);
        }
        if (entity.Status != DataExportStatus.Available || entity.File is null)
        {
            return new(Failure: AccessDataExportFailure.NotReady);
        }

        if (await objectStorage.InspectAsync(entity.File.ObjectKey, cancellationToken) is null)
            return new(Failure: AccessDataExportFailure.ObjectMissing);
        var content = await objectStorage.OpenReadAsync(entity.File.ObjectKey, cancellationToken);
        return new(new DataExportDownload(
            content,
            entity.File.FileName,
            entity.File.ContentType,
            entity.File.ByteLength,
            Convert.ToHexString(entity.File.Sha256)));
    }

    internal static DataExportView Map(DataExport item) => new(
        item.Id,
        item.Scope,
        item.CompetitionId,
        item.RequestedByUserId,
        item.RequestedAt,
        item.IncludeProtectedFlags,
        item.Reason,
        item.Status,
        item.StartedAt,
        item.CompletedAt,
        item.ExpiresAt,
        item.FileId,
        item.File?.FileName,
        item.File?.ContentType,
        item.File?.ByteLength,
        item.File is null ? null : Convert.ToHexString(item.File.Sha256),
        item.FailureCode,
        item.FailureDetail);
}
