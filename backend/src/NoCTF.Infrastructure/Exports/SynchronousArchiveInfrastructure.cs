using System.Data;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Exports;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Exports;

public sealed class SynchronousArchiveOptions
{
    public const string SectionName = "SynchronousExports";

    public string TemporaryDirectory { get; init; } =
        Path.Combine(Path.GetTempPath(), "noctf-synchronous-exports");

    public long MaxRecords { get; init; } = 250_000;

    public long MaxCompressedBytes { get; init; } = 512L * 1024 * 1024;

    public long MaxWorkingSetBytes { get; init; } = 8L * 1024 * 1024;

    public int MaxDurationSeconds { get; init; } = 120;
}

internal static class SynchronousArchiveInfrastructure
{
    internal static IServiceCollection AddNoCtfSynchronousArchives(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SynchronousArchiveOptions>()
            .Bind(configuration.GetSection(SynchronousArchiveOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.TemporaryDirectory)
                    && options.MaxRecords > 0
                    && options.MaxCompressedBytes > 0
                    && options.MaxWorkingSetBytes > 0
                    && options.MaxDurationSeconds > 0,
                "SynchronousExports limits and temporary directory must be configured.")
            .ValidateOnStart();
        services.AddScoped<ISynchronousArchiveGenerator, PostgresSynchronousArchiveGenerator>();
        services.AddScoped<ExportCompetitionArchive>();
        services.AddScoped<ExportPlatformAuditArchive>();
        return services;
    }
}

public sealed class PostgresSynchronousArchiveGenerator(
    NoCtfDbContext db,
    IPlatformAuditLogStore platformAudits,
    IOptions<SynchronousArchiveOptions> configuredOptions,
    TimeProvider timeProvider,
    ILogger<PostgresSynchronousArchiveGenerator> log) : ISynchronousArchiveGenerator
{
    private const string ZipContentType = "application/zip";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ArchiveLimits limits = ArchiveLimits.From(configuredOptions.Value);
    private readonly string temporaryDirectory = configuredOptions.Value.TemporaryDirectory;

    public async Task<SynchronousArchiveResult> GenerateCompetitionAsync(
        ExportCompetitionArchiveCommand command,
        CancellationToken cancellationToken)
    {
        var accessFailure = await CheckCompetitionAccessAsync(command, cancellationToken);
        if (accessFailure is not null)
            return new(Failure: accessFailure);

        return await GenerateAsync(
            $"competition-{command.CompetitionId:N}-{timeProvider.GetUtcNow():yyyyMMddHHmmss}.zip",
            (archive, budget, token) => WriteCompetitionArchiveAsync(
                archive,
                budget,
                command,
                token),
            cancellationToken);
    }

    public Task<SynchronousArchiveResult> GeneratePlatformAuditAsync(
        ExportPlatformAuditArchiveCommand command,
        CancellationToken cancellationToken) =>
        GenerateAsync(
            $"platform-audit-{timeProvider.GetUtcNow():yyyyMMddHHmmss}.zip",
            (archive, budget, token) => WritePlatformAuditArchiveAsync(
                archive,
                budget,
                command,
                token),
            cancellationToken);

    private async Task<SynchronousArchiveResult> GenerateAsync(
        string fileName,
        Func<ZipArchive, ArchiveBudget, CancellationToken, Task<SynchronousArchiveFailure?>> write,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.zip");
        using var deadline = new CancellationTokenSource(
            limits.MaximumDuration,
            timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token);
        var transferred = false;
        try
        {
            var budget = new ArchiveBudget(limits);
            await using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var limited = new CompressedSizeLimitedStream(
                    output,
                    limits.MaximumCompressedBytes);
                using (var archive = new ZipArchive(
                    limited,
                    ZipArchiveMode.Create,
                    leaveOpen: true))
                {
                    var failure = await write(archive, budget, linked.Token);
                    if (failure is not null)
                        return new(Failure: failure);
                }
                await limited.FlushAsync(linked.Token);
            }

            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous
                | FileOptions.SequentialScan
                | FileOptions.DeleteOnClose);
            transferred = true;
            return new(new SynchronousArchive(stream, fileName, ZipContentType));
        }
        catch (ArchiveLimitException exception)
        {
            return new(Failure: exception.Failure);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
            && deadline.IsCancellationRequested)
        {
            return new(Failure: SynchronousArchiveFailure.TimeLimitExceeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(exception, "Synchronous archive generation failed.");
            return new(Failure: SynchronousArchiveFailure.GenerationFailed);
        }
        finally
        {
            if (!transferred && File.Exists(path))
                File.Delete(path);
        }
    }

    private async Task<SynchronousArchiveFailure?> WriteCompetitionArchiveAsync(
        ZipArchive archive,
        ArchiveBudget budget,
        ExportCompetitionArchiveCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var access = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == command.CompetitionId && item.DeletedAt == null)
            .Select(item => new
            {
                Competition = item,
                CanExport = command.RequesterIsAdministrator
                    || item.OwnerId == command.RequestedByUserId
                    || item.ManagerIds.Contains(command.RequestedByUserId)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (access is null)
            return SynchronousArchiveFailure.SubjectNotFound;
        if (!access.CanExport)
            return SynchronousArchiveFailure.Forbidden;
        var competition = access.Competition;
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        counts["competition.ndjson"] = await WriteRowsAsync(
            archive,
            "competition.ndjson",
            Single(new
            {
                competition.Id,
                competition.Title,
                competition.Description,
                competition.PosterFileId,
                competition.OwnerId,
                competition.ManagerIds,
                competition.JudgeIds,
                competition.ObserverIds,
                competition.AccessMode,
                competition.Mode,
                competition.ConfigurationJson,
                competition.TracksEnabled,
                competition.TrackConfigurationJson,
                competition.FrozenStartAt,
                competition.HiddenStartAt,
                competition.StartAt,
                competition.EndAt,
                competition.Status,
                competition.TeamRegistrationAutoApprove,
                competition.AllowTeamRegistrationWhileRunning,
                competition.PracticeModeEnabled,
                competition.MaxTeamMembers,
                competition.MaxConcurrentRuntimeInstancesPerTeam,
                competition.MaxActiveQuestionsPerTeam,
                competition.MaxParticipantMessagesBeforeHandlerReply,
                competition.AllowChallengeOwnersToHandleQuestions,
                competition.CreatedAt,
                competition.UpdatedAt
            }),
            budget,
            cancellationToken);

        var challengeIds = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CompetitionId == command.CompetitionId)
            .Select(item => item.ChallengeId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var competitionChallengeIds = await db.CompetitionChallenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.CompetitionId == command.CompetitionId)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);

        counts["competition-challenges.ndjson"] = await WriteRowsAsync(
            archive,
            "competition-challenges.ndjson",
            db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.CompetitionId == command.CompetitionId)
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id)
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);
        counts["challenge-templates.ndjson"] = await WriteRowsAsync(
            archive,
            "challenge-templates.ndjson",
            db.Challenges.IgnoreQueryFilters().AsNoTracking()
                .Where(item => challengeIds.Contains(item.Id))
                .OrderBy(item => item.Id)
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);
        counts["challenge-attachments.ndjson"] = await WriteRowsAsync(
            archive,
            "challenge-attachments.ndjson",
            db.Set<ChallengeAttachment>().IgnoreQueryFilters().AsNoTracking()
                .Where(item => challengeIds.Contains(item.ChallengeId))
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.ChallengeId,
                    item.FileId,
                    item.File.FileName,
                    item.File.ContentType,
                    item.File.ByteLength,
                    item.File.Sha256,
                    item.CreatedAt,
                    item.DeletedAt
                })
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);
        counts["challenge-flags.ndjson"] = await WriteRowsAsync(
            archive,
            "challenge-flags.ndjson",
            db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.ChallengeId != null && challengeIds.Contains(item.ChallengeId.Value)
                    || item.CompetitionChallengeId != null
                    && competitionChallengeIds.Contains(item.CompetitionChallengeId.Value))
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.ChallengeId,
                    item.CompetitionChallengeId,
                    item.TeamId,
                    item.FlagSha256,
                    Flag = command.IncludeProtectedFlags ? item.Flag : null,
                    item.MatchKind,
                    item.SpecificationKind,
                    item.SpecificationId,
                    item.ValidStart,
                    item.ValidUntil,
                    item.CreatedAt,
                    item.DeletedAt
                })
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);
        counts["teams.ndjson"] = await WriteRowsAsync(
            archive,
            "teams.ndjson",
            db.Teams.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.CompetitionId == command.CompetitionId)
                .OrderBy(item => item.RegisteredAt)
                .ThenBy(item => item.Id)
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);
        counts["gameplay-facts.ndjson"] = await WriteRowsAsync(
            archive,
            "gameplay-facts.ndjson",
            db.GameplayFacts.AsNoTracking()
                .Where(item => item.CompetitionId == command.CompetitionId)
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.CompetitionId,
                    item.CompetitionChallengeId,
                    item.TeamId,
                    item.VictimTeamId,
                    item.ActorUserId,
                    item.Kind,
                    item.OccurredAt,
                    item.ReferenceKind,
                    item.ReferenceId,
                    Value = command.IncludeProtectedFlags ? item.Value : null,
                    item.ValueSha256,
                    item.State,
                    item.Result,
                    item.FailureCode,
                    item.UpdatedAt
                })
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);
        counts["runtime-instances.ndjson"] = await WriteRowsAsync(
            archive,
            "runtime-instances.ndjson",
            db.RuntimeInstances.AsNoTracking()
                .Where(item => item.CompetitionId == command.CompetitionId)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.CompetitionId,
                    item.CompetitionChallengeId,
                    item.TeamId,
                    item.Purpose,
                    item.GameplayFactId,
                    item.RuntimeKind,
                    item.RuntimeProvider,
                    item.RunnerId,
                    item.State,
                    item.FailureCode,
                    item.CreatedAt,
                    item.RunningAt,
                    item.ExpiresAt,
                    item.StoppedAt
                })
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);
        counts["patch-uploads.ndjson"] = await WriteRowsAsync(
            archive,
            "patch-uploads.ndjson",
            db.PatchUploads.AsNoTracking()
                .Where(item => item.CompetitionId == command.CompetitionId)
                .OrderBy(item => item.UploadedAt)
                .ThenBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.CompetitionId,
                    item.CompetitionChallengeId,
                    item.TeamId,
                    item.UploadedByUserId,
                    item.RuntimeInstanceId,
                    item.FileId,
                    item.File.FileName,
                    item.File.ContentType,
                    item.File.ByteLength,
                    item.File.Sha256,
                    item.UploadedAt
                })
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);
        counts["competition-events.ndjson"] = await WriteRowsAsync(
            archive,
            "competition-events.ndjson",
            db.CompetitionEvents.AsNoTracking()
                .Where(item => item.CompetitionId == command.CompetitionId)
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .AsAsyncEnumerable(),
            budget,
            cancellationToken);

        await WriteRowsAsync(
            archive,
            "manifest.json",
            Single(new
            {
                SchemaVersion = "noctf.competition-export/2",
                command.CompetitionId,
                ExportedAt = timeProvider.GetUtcNow(),
                command.IncludeProtectedFlags,
                Entries = counts.Select(item => new
                {
                    Name = item.Key,
                    RowCount = item.Value
                })
            }),
            budget,
            cancellationToken,
            appendNewline: false);
        await transaction.CommitAsync(cancellationToken);

        db.CompetitionEvents.Add(new CompetitionEvent
        {
            Id = Guid.CreateVersion7(timeProvider.GetUtcNow()),
            CompetitionId = command.CompetitionId,
            Kind = CompetitionEventKind.CompetitionArchiveExported,
            Level = command.IncludeProtectedFlags
                ? CompetitionEventLevel.Warning
                : CompetitionEventLevel.Information,
            Visibility = CompetitionEventVisibility.Staff,
            ActorUserId = command.RequestedByUserId,
            SubjectType = EntityReferenceKind.Competition,
            SubjectId = command.CompetitionId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                command.IncludeProtectedFlags,
                reason = command.IncludeProtectedFlags ? command.Reason : null
            }, JsonOptions),
            OccurredAt = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<SynchronousArchiveFailure?> CheckCompetitionAccessAsync(
        ExportCompetitionArchiveCommand command,
        CancellationToken cancellationToken)
    {
        var access = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == command.CompetitionId && item.DeletedAt == null)
            .Select(item => new
            {
                CanExport = command.RequesterIsAdministrator
                    || item.OwnerId == command.RequestedByUserId
                    || item.ManagerIds.Contains(command.RequestedByUserId)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (access is null)
            return SynchronousArchiveFailure.SubjectNotFound;
        return access.CanExport ? null : SynchronousArchiveFailure.Forbidden;
    }

    private async Task<SynchronousArchiveFailure?> WritePlatformAuditArchiveAsync(
        ZipArchive archive,
        ArchiveBudget budget,
        ExportPlatformAuditArchiveCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        DateTimeOffset? beforeOccurredAt = null;
        Guid? beforeId = null;
        long count = 0;
        var pageSize = (int)Math.Clamp(
            limits.MaximumWorkingSetBytes / 4096,
            1,
            2_000);
        var entry = archive.CreateEntry("audit.ndjson", CompressionLevel.SmallestSize);
        await using (var output = entry.Open())
        {
            while (true)
            {
                var page = await platformAudits.QueryAsync(new PlatformAuditQuery(
                    command.Kind,
                    command.From,
                    command.To,
                    command.CompetitionId,
                    command.ActorId,
                    beforeOccurredAt,
                    beforeId,
                    pageSize), cancellationToken);
                if (page.Count == 0)
                    break;
                foreach (var row in page)
                {
                    await WriteJsonLineAsync(output, row, budget, cancellationToken);
                    count++;
                }
                if (page.Count < pageSize)
                    break;
                var last = page[^1];
                beforeOccurredAt = last.OccurredAt;
                beforeId = last.Id;
            }
        }

        await WriteRowsAsync(
            archive,
            "manifest.json",
            Single(new
            {
                SchemaVersion = "noctf.platform-audit-export/1",
                ExportedAt = timeProvider.GetUtcNow(),
                command.Kind,
                command.CompetitionId,
                command.ActorId,
                command.From,
                command.To,
                Entries = new[] { new { Name = "audit.ndjson", RowCount = count } }
            }),
            budget,
            cancellationToken,
            appendNewline: false);
        await transaction.CommitAsync(cancellationToken);

        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(timeProvider.GetUtcNow()),
            SourceType = NotificationSourceType.User,
            SourceId = command.RequestedByUserId,
            TargetType = NotificationTargetType.PlatformAdministrators,
            TargetId = Notification.PlatformAdministratorsTargetId,
            Kind = NotificationKind.PlatformAuditExported,
            ContentJson = JsonSerializer.Serialize(new PlatformAuditArchiveExportedFact(
                SchemaVersion: 1,
                command.Kind,
                command.CompetitionId,
                command.ActorId,
                command.From,
                command.To,
                RecordCount: count), JsonOptions),
            RelatedType = command.CompetitionId is null
                ? EntityReferenceKind.Platform
                : EntityReferenceKind.Competition,
            RelatedId = command.CompetitionId
                ?? Notification.PlatformAdministratorsTargetId,
            SentAt = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static async IAsyncEnumerable<T> Single<T>(T value)
    {
        yield return value;
        await Task.CompletedTask;
    }

    private static async Task<long> WriteRowsAsync<T>(
        ZipArchive archive,
        string name,
        IAsyncEnumerable<T> rows,
        ArchiveBudget budget,
        CancellationToken cancellationToken,
        bool appendNewline = true)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        await using var output = entry.Open();
        long count = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await WriteJsonAsync(output, row, budget, cancellationToken, appendNewline);
            count++;
        }
        return count;
    }

    private static Task WriteJsonLineAsync<T>(
        Stream output,
        T row,
        ArchiveBudget budget,
        CancellationToken cancellationToken) =>
        WriteJsonAsync(output, row, budget, cancellationToken, appendNewline: true);

    private static async Task WriteJsonAsync<T>(
        Stream output,
        T row,
        ArchiveBudget budget,
        CancellationToken cancellationToken,
        bool appendNewline)
    {
        budget.Record();
        await using var serialized = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            serialized,
            row,
            JsonOptions,
            cancellationToken);
        if (appendNewline)
            serialized.WriteByte((byte)'\n');
        budget.CheckWorkingSet(serialized.Length);
        serialized.Position = 0;
        await serialized.CopyToAsync(output, cancellationToken);
    }

    private sealed record ArchiveLimits(
        long MaximumRecords,
        long MaximumCompressedBytes,
        long MaximumWorkingSetBytes,
        TimeSpan MaximumDuration)
    {
        internal static ArchiveLimits From(SynchronousArchiveOptions options) => new(
            options.MaxRecords,
            options.MaxCompressedBytes,
            options.MaxWorkingSetBytes,
            TimeSpan.FromSeconds(options.MaxDurationSeconds));
    }

    private sealed class ArchiveBudget(ArchiveLimits limits)
    {
        private long records;

        internal void Record()
        {
            if (++records > limits.MaximumRecords)
            {
                throw new ArchiveLimitException(
                    SynchronousArchiveFailure.RecordLimitExceeded);
            }
        }

        internal void CheckWorkingSet(long bytes)
        {
            if (bytes > limits.MaximumWorkingSetBytes)
            {
                throw new ArchiveLimitException(
                    SynchronousArchiveFailure.MemoryLimitExceeded);
            }
        }
    }

    private sealed class CompressedSizeLimitedStream(
        Stream inner,
        long maximumBytes) : Stream
    {
        private long written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written;
        public override long Position
        {
            get => written;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            inner.Write(buffer, offset, count);
            written += count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken);
            written += buffer.Length;
        }

        private void EnsureCapacity(int count)
        {
            if (written > maximumBytes - count)
            {
                throw new ArchiveLimitException(
                    SynchronousArchiveFailure.CompressedSizeLimitExceeded);
            }
        }

        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class ArchiveLimitException(
        SynchronousArchiveFailure failure) : Exception(failure.ToString())
    {
        internal SynchronousArchiveFailure Failure { get; } = failure;
    }
}
