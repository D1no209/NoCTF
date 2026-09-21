using System.IO.Compression;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Access;

public sealed class RuntimeTrafficCaptureStore(
    NoCtfDbContext db,
    IStore objects,
    ICompetitionEventRecorder events,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider) : IRuntimeTrafficCaptureStore
{
    public async Task<RuntimeTrafficCapturePage> ListAsync(
        RuntimeTrafficCaptureQuery query,
        CancellationToken cancellationToken)
    {
        var source = ActiveSegments(query.CompetitionId)
            .Join(
                db.RuntimeInstances.AsNoTracking(),
                capture => capture.SubjectId,
                runtime => runtime.Id,
                (capture, runtime) => new { Capture = capture, Runtime = runtime })
            .Join(
                db.Files.AsNoTracking(),
                item => item.Capture.RelatedId,
                file => (Guid?)file.Id,
                (item, file) => new { item.Capture, item.Runtime, File = file });
        if (query.CompetitionChallengeId is Guid challengeId)
            source = source.Where(item => item.Runtime.CompetitionChallengeId == challengeId);
        if (query.TeamId is Guid teamId)
            source = source.Where(item => item.Runtime.TeamId == teamId);
        if (query.RuntimeInstanceId is Guid runtimeInstanceId)
            source = source.Where(item => item.Runtime.Id == runtimeInstanceId);
        var grouped = source.GroupBy(item => new
        {
            item.Runtime.Id,
            item.Runtime.CompetitionChallengeId,
            item.Runtime.TeamId,
            item.Runtime.State
        });
        if (query.Truncated is bool truncated)
        {
            grouped = grouped.Where(group => group.Any(item => EF.Functions.JsonContains(
                    item.Capture.PayloadJson,
                    "{\"truncated\":true}")) == truncated);
        }
        var total = await grouped.CountAsync(cancellationToken);
        var page = await grouped
            .Select(group => new RuntimeTrafficCaptureView(
                group.Key.Id,
                group.Key.CompetitionChallengeId!.Value,
                group.Key.TeamId,
                group.Key.State,
                group.Count(),
                group.Sum(item => item.File.ByteLength),
                group.Min(item => item.Capture.OccurredAt),
                group.Max(item => item.File.CreatedAt),
                group.Any(item => EF.Functions.JsonContains(
                    item.Capture.PayloadJson,
                    "{\"truncated\":true}"))))
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.RuntimeInstanceId)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToArrayAsync(cancellationToken);
        if (page.Length == 0)
            return new(page, total);

        var teamIds = page.Select(item => item.TeamId).OfType<Guid>().Distinct().ToArray();
        var teamNames = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);
        var challengeIds = page.Select(item => item.CompetitionChallengeId).Distinct().ToArray();
        var challengeTitles = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            .Where(challenge => challengeIds.Contains(challenge.Id))
            .Join(
                db.Challenges.IgnoreQueryFilters().AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new
                {
                    challenge.Id,
                    Title = challenge.CustomTitle ?? template.Title
                })
            .ToDictionaryAsync(item => item.Id, item => item.Title, cancellationToken);
        return new(
            page.Select(item => item with
            {
                TeamName = item.TeamId is Guid teamId
                    ? teamNames.GetValueOrDefault(teamId)
                    : null,
                ChallengeTitle = challengeTitles.GetValueOrDefault(
                    item.CompetitionChallengeId)
            }).ToArray(),
            total);
    }

    public async Task<RuntimeTrafficCaptureFile?> OpenAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        CancellationToken cancellationToken)
    {
        var segments = await SegmentFiles(competitionId, runtimeInstanceId)
            .ToArrayAsync(cancellationToken);
        if (segments.Length == 0)
            return null;
        var streams = new List<Stream>(segments.Length);
        try
        {
            foreach (var segment in segments)
                streams.Add(await objects.OpenRead(segment.ObjectKey, cancellationToken));
            return new(
                new ConcatenatedReadStream(streams),
                $"runtime-{runtimeInstanceId:N}-traffic.pcapng");
        }
        catch
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
            throw;
        }
    }

    public async Task<RuntimeTrafficCaptureArchive?> ExportAsync(
        Guid competitionId,
        IReadOnlyList<Guid> runtimeInstanceIds,
        CancellationToken cancellationToken)
    {
        var ids = runtimeInstanceIds.Distinct().ToArray();
        if (ids.Length == 0)
            return null;
        var segments = await ActiveSegments(competitionId)
            .Where(capture => ids.Contains(capture.SubjectId))
            .OrderBy(capture => capture.SubjectId)
            .ThenBy(capture => capture.OccurredAt)
            .ThenBy(capture => capture.Id)
            .Join(
                db.Files.AsNoTracking(),
                capture => capture.RelatedId,
                file => (Guid?)file.Id,
                (capture, file) => new
                {
                    RuntimeInstanceId = capture.SubjectId,
                    file.ObjectKey
                })
            .ToArrayAsync(cancellationToken);
        if (segments.Length == 0)
            return null;
        var grouped = segments
            .GroupBy(segment => segment.RuntimeInstanceId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(segment => segment.ObjectKey).ToArray());
        return new(
            (output, token) => WriteArchiveAsync(output, grouped, token),
            $"runtime-traffic-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}.zip");
    }

    public async Task<RuntimeTrafficCaptureDeleteState> DeleteAsync(
        Guid competitionId,
        Guid runtimeInstanceId,
        Guid actorUserId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(candidate => candidate.Id == runtimeInstanceId
                && candidate.CompetitionId == competitionId)
            .Select(candidate => new { candidate.Id, candidate.State })
            .SingleOrDefaultAsync(cancellationToken);
        if (runtime is null)
            return RuntimeTrafficCaptureDeleteState.NotFound;
        if (runtime.State is RuntimeState.Queued or RuntimeState.Provisioning
            or RuntimeState.Running or RuntimeState.Stopping)
            return RuntimeTrafficCaptureDeleteState.RuntimeActive;

        var segments = await ActiveSegments(competitionId)
            .Where(capture => capture.SubjectId == runtimeInstanceId)
            .Select(capture => new
            {
                capture.Id,
                FileId = capture.RelatedId!.Value
            })
            .ToArrayAsync(cancellationToken);
        if (segments.Length == 0)
            return RuntimeTrafficCaptureDeleteState.NotFound;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var segment in segments)
        {
            await events.RecordAsync(new(
                competitionId,
                CompetitionEventKind.RuntimeTrafficCaptureDeleted,
                CompetitionEventLevel.Warning,
                CompetitionEventVisibility.Staff,
                deletedAt,
                ActorUserId: actorUserId,
                RuntimeInstanceId: runtimeInstanceId,
                ParentEventId: segment.Id,
                SubjectType: EntityReferenceKind.RuntimeInstance,
                SubjectId: runtimeInstanceId,
                RelatedType: EntityReferenceKind.File,
                RelatedId: segment.FileId), cancellationToken);
            await outbox.PublishAsync(new CleanupFile(segment.FileId));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
        return RuntimeTrafficCaptureDeleteState.Deleted;
    }

    private IQueryable<CompetitionEvent> ActiveSegments(Guid competitionId) =>
        db.CompetitionEvents.AsNoTracking()
            .Where(capture => capture.CompetitionId == competitionId
                && capture.Kind == CompetitionEventKind.RuntimeTrafficCaptureStored
                && capture.SubjectType == EntityReferenceKind.RuntimeInstance
                && capture.RelatedType == EntityReferenceKind.File
                && capture.RelatedId != null
                && !db.CompetitionEvents.Any(deleted =>
                    deleted.CompetitionId == competitionId
                    && deleted.Kind == CompetitionEventKind.RuntimeTrafficCaptureDeleted
                    && deleted.ParentEventId == capture.Id));

    private IQueryable<NoCTF.Domain.Storage.StoredFile> SegmentFiles(
        Guid competitionId,
        Guid runtimeInstanceId) =>
        ActiveSegments(competitionId)
            .Where(capture => capture.SubjectId == runtimeInstanceId)
            .OrderBy(capture => capture.OccurredAt)
            .ThenBy(capture => capture.Id)
            .Join(
                db.Files.AsNoTracking(),
                capture => capture.RelatedId,
                file => (Guid?)file.Id,
                (_, file) => file);

    private async Task WriteArchiveAsync(
        Stream output,
        IReadOnlyDictionary<Guid, string[]> segments,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (runtimeInstanceId, objectKeys) in segments)
        {
            var entry = archive.CreateEntry(
                $"runtime-{runtimeInstanceId:N}-traffic.pcapng",
                CompressionLevel.Fastest);
            await using var destination = entry.Open();
            foreach (var objectKey in objectKeys)
            {
                await using var source = await objects.OpenRead(
                    objectKey,
                    cancellationToken);
                await source.CopyToAsync(destination, cancellationToken);
            }
        }
    }

    private sealed class ConcatenatedReadStream(IReadOnlyList<Stream> streams) : Stream
    {
        private int index;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
                return 0;
            while (index < streams.Count)
            {
                var read = await streams[index].ReadAsync(buffer, cancellationToken);
                if (read > 0)
                    return read;
                await streams[index++].DisposeAsync();
            }
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (; index < streams.Count; index++)
                    streams[index].Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            for (; index < streams.Count; index++)
                await streams[index].DisposeAsync();
            GC.SuppressFinalize(this);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
