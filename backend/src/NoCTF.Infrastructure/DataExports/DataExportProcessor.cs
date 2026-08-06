using System.Data;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.DataExports;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.DataExports;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.DataExports;

public sealed class DataExportProcessor(
    NoCtfDbContext db,
    IObjectStorage objectStorage,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder competitionEvents,
    IPlatformAuditLogStore platformAudits,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<DataExportProcessor> log) : IDataExportProcessor
{
    private const string ZipContentType = "application/zip";
    private const string NdjsonContentType = "application/x-ndjson";
    private const long DefaultMaximumArchiveBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly CompetitionEventKind[] CheatResolutionKinds =
    [
        CompetitionEventKind.CheatIncidentConfirmed,
        CompetitionEventKind.CheatIncidentDismissed,
        CompetitionEventKind.CheatIncidentSuperseded,
        CompetitionEventKind.CheatIncidentCorrected
    ];

    private readonly long maximumArchiveBytes = configuration.GetValue<long?>(
        "DataExports:MaxArchiveBytes") is > 0 and var configured
        ? configured
        : DefaultMaximumArchiveBytes;

    public async Task GenerateAsync(
        Guid dataExportId,
        CancellationToken cancellationToken)
    {
        var job = await db.DataExports.SingleOrDefaultAsync(
            item => item.Id == dataExportId,
            cancellationToken);
        if (job is null
            || job.Status is not (DataExportStatus.Queued or DataExportStatus.Processing))
            return;

        job.Status = DataExportStatus.Processing;
        job.StartedAt ??= timeProvider.GetUtcNow();
        job.FailureCode = null;
        job.FailureDetail = null;
        await db.SaveChangesAsync(cancellationToken);

        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"noctf-data-export-{job.Id:N}.tmp");
        StoredObject? storedObject = null;
        try
        {
            var artifact = await GenerateArtifactAsync(job, temporaryPath, cancellationToken);
            try
            {
                await using var input = File.OpenRead(temporaryPath);
                storedObject = await objectStorage.PutAsync(
                        artifact.ObjectKey,
                        artifact.FileName,
                        artifact.ContentType,
                        input,
                        cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new DataExportGenerationException(
                    DataExportFailureCode.ObjectStorageFailed,
                    "The export could not be stored.",
                    exception);
            }

            await CompleteAsync(job.Id, storedObject, cancellationToken);
        }
        catch (DataExportGenerationException exception)
        {
            if (storedObject is not null)
                await TryDeleteObjectAsync(storedObject.ObjectKey, cancellationToken);
            await FailAsync(job.Id, exception.FailureCode, exception.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (storedObject is not null)
                await TryDeleteObjectAsync(storedObject.ObjectKey, cancellationToken);
            log.LogError(exception, "Data export {DataExportId} generation failed.", job.Id);
            await FailAsync(
                job.Id,
                storedObject is null
                    ? DataExportFailureCode.GenerationFailed
                    : DataExportFailureCode.ObjectStorageFailed,
                "The export could not be generated.",
                cancellationToken);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task ExpireAsync(
        Guid dataExportId,
        CancellationToken cancellationToken)
    {
        var job = await db.DataExports.SingleOrDefaultAsync(
            item => item.Id == dataExportId,
            cancellationToken);
        if (job is null || job.Status == DataExportStatus.Expired)
            return;
        var now = timeProvider.GetUtcNow();
        if (job.ExpiresAt is null || job.Status != DataExportStatus.Available)
            return;
        if (job.ExpiresAt > now)
        {
            await outbox.ScheduleAsync(new ExpireDataExport(job.Id), job.ExpiresAt.Value);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        if (job.ObjectKey is not null)
            await objectStorage.DeleteAsync(job.ObjectKey, cancellationToken);
        job.Status = DataExportStatus.Expired;
        job.ActiveSlot = null;
        job.ObjectKey = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PurgeAsync(
        Guid dataExportId,
        CancellationToken cancellationToken)
    {
        var job = await db.DataExports.SingleOrDefaultAsync(
            item => item.Id == dataExportId,
            cancellationToken);
        if (job is null)
            return;
        var now = timeProvider.GetUtcNow();
        if (job.PurgeAt > now)
        {
            await outbox.ScheduleAsync(new PurgeDataExport(job.Id), job.PurgeAt);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        if (job.ObjectKey is not null)
            await objectStorage.DeleteAsync(job.ObjectKey, cancellationToken);
        db.DataExports.Remove(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<GeneratedArtifact> GenerateArtifactAsync(
        DataExport job,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        GeneratedArtifact artifact;
        if (job.Scope == DataExportScope.CompetitionArchive
            && job.CompetitionId is Guid competitionId)
        {
            artifact = await GenerateCompetitionArchiveAsync(
                job,
                competitionId,
                temporaryPath,
                cancellationToken);
        }
        else if (job.Scope == DataExportScope.PlatformAudit)
        {
            artifact = await GeneratePlatformAuditAsync(
                job,
                temporaryPath,
                cancellationToken);
        }
        else
        {
            throw new DataExportGenerationException(
                DataExportFailureCode.GenerationFailed,
                "The export scope is invalid.");
        }
        await transaction.CommitAsync(cancellationToken);
        return artifact;
    }

    private async Task<GeneratedArtifact> GenerateCompetitionArchiveAsync(
        DataExport job,
        Guid competitionId,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.LifecycleAudits)
            .Include(item => item.LeaderboardVisibilityAudits)
            .SingleOrDefaultAsync(item => item.Id == competitionId, cancellationToken);
        if (competition is null)
        {
            throw new DataExportGenerationException(
                DataExportFailureCode.SubjectNotFound,
                "The competition no longer exists.");
        }

        await using var output = File.Create(temporaryPath);
        await using var limited = new LengthLimitedWriteStream(output, maximumArchiveBytes);
        using var archive = new ZipArchive(limited, ZipArchiveMode.Create, leaveOpen: true);
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);

        counts["competition.ndjson"] = await WriteRowsAsync(
            archive,
            "competition.ndjson",
            new[]
            {
                new
                {
                    competition.Id,
                    competition.Title,
                    competition.Description,
                    competition.OwnerId,
                    competition.ManagerIds,
                    competition.JudgeIds,
                    competition.ObserverIds,
                    competition.PermissionRevision,
                    competition.Mode,
                    competition.ConfigurationJson,
                    competition.ConfigurationRevision,
                    competition.ConfigurationUpdatedAt,
                    competition.LeaderboardRevision,
                    competition.LeaderboardVisibility,
                    competition.LeaderboardVisibilityStartsAt,
                    competition.LeaderboardVisibilityAppliedAt,
                    competition.LeaderboardVisibilityRevision,
                    competition.FrozenLeaderboardSnapshotJson,
                    competition.StartAt,
                    competition.EndAt,
                    competition.RunningSince,
                    competition.AccumulatedRunningSeconds,
                    competition.Status,
                    competition.TeamRegistrationAutoApprove,
                    competition.MaxTeamMembers,
                    competition.MaxConcurrentRuntimeInstancesPerTeam,
                    competition.CreatedAt,
                    competition.UpdatedAt,
                    competition.DeletedAt,
                    LifecycleAudits = competition.LifecycleAudits
                        .OrderBy(item => item.OccurredAt)
                        .ThenBy(item => item.Id)
                        .Select(item => new
                        {
                            item.Id,
                            item.From,
                            item.To,
                            item.ActorId,
                            item.Reason,
                            item.Automatic,
                            item.OccurredAt
                        }),
                    LeaderboardVisibilityAudits = competition.LeaderboardVisibilityAudits
                        .OrderBy(item => item.OccurredAt)
                        .ThenBy(item => item.Id)
                        .Select(item => new
                        {
                            item.Id,
                            item.From,
                            item.To,
                            item.ActorId,
                            item.Reason,
                            item.Automatic,
                            item.OccurredAt
                        })
                }
            },
            cancellationToken);

        counts["challenges.ndjson"] = await WriteChallengesAsync(
            archive,
            competitionId,
            job.IncludeProtectedFlags,
            cancellationToken);
        counts["teams.ndjson"] = await WriteAsyncRowsAsync(
            archive,
            "teams.ndjson",
            db.Teams.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.CompetitionId == competitionId)
                .OrderBy(item => item.RegisteredAt)
                .ThenBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.CompetitionId,
                    item.Name,
                    item.AvatarUrl,
                    item.CaptainId,
                    item.MemberIds,
                    item.IsLocked,
                    item.RegistrationStatus,
                    item.RegisteredAt,
                    item.IsBanned,
                    item.BannedAt,
                    item.BannedById,
                    item.BanReason,
                    item.DeletedAt
                })
                .AsAsyncEnumerable(),
            cancellationToken);
        counts["submissions.ndjson"] = await WriteSubmissionsAsync(
            archive,
            competitionId,
            job.IncludeProtectedFlags,
            cancellationToken);
        counts["scoring-events.ndjson"] = await WriteAsyncRowsAsync(
            archive,
            "scoring-events.ndjson",
            db.ScoringEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.CompetitionId == competitionId)
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.CompetitionId,
                    item.TeamId,
                    item.VictimTeamId,
                    item.CompetitionChallengeId,
                    item.SubmissionId,
                    item.Kind,
                    item.Result,
                    item.FailureCode,
                    item.SpecificationKind,
                    item.SpecificationId,
                    item.ProcessingVersion,
                    item.CompetitionConfigurationRevision,
                    item.CompetitionChallengeRevision,
                    item.OccurredAt,
                    item.CreatedAt,
                    item.DeletedAt
                })
                .AsAsyncEnumerable(),
            cancellationToken);
        counts["cheat-incidents.ndjson"] = await WriteCheatIncidentsAsync(
            archive,
            competitionId,
            cancellationToken);
        counts["competition-events.ndjson"] = await WriteAsyncRowsAsync(
            archive,
            "competition-events.ndjson",
            db.CompetitionEvents.AsNoTracking()
                .Where(item => item.CompetitionId == competitionId)
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .AsAsyncEnumerable(),
            cancellationToken);

        await WriteRowsAsync(
            archive,
            "manifest.json",
            new[]
            {
                new
                {
                    SchemaVersion = "noctf.competition-export/1",
                    ExportId = job.Id,
                    job.Scope,
                    job.CompetitionId,
                    ExportedAt = timeProvider.GetUtcNow(),
                    job.IncludeProtectedFlags,
                    Entries = counts.Select(item => new
                    {
                        Name = item.Key,
                        RowCount = item.Value
                    })
                }
            },
            cancellationToken,
            appendNewline: false);
        archive.Dispose();
        await limited.FlushAsync(cancellationToken);

        var fileName = $"competition-{competitionId:N}-{job.RequestedAt:yyyyMMddHHmmss}.zip";
        return new(
            $"data-exports/{job.Id:N}.zip",
            fileName,
            ZipContentType);
    }

    private async Task<long> WriteChallengesAsync(
        ZipArchive archive,
        Guid competitionId,
        bool includeProtectedFlags,
        CancellationToken cancellationToken)
    {
        var competitionChallenges = await db.CompetitionChallenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.CompetitionId == competitionId)
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var challengeIds = competitionChallenges.Select(item => item.ChallengeId).Distinct().ToArray();
        var competitionChallengeIds = competitionChallenges.Select(item => item.Id).ToArray();
        var challenges = await db.Challenges.IgnoreQueryFilters().AsNoTracking()
            .Where(item => challengeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var hints = await db.Set<CompetitionChallengeHint>().AsNoTracking()
            .Where(item => competitionChallengeIds.Contains(item.CompetitionChallengeId))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var attachments = await db.Set<ChallengeAttachment>().AsNoTracking()
            .Where(item => challengeIds.Contains(item.ChallengeId))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var flags = await db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.ChallengeId != null && challengeIds.Contains(item.ChallengeId.Value)
                || item.CompetitionChallengeId != null
                && competitionChallengeIds.Contains(item.CompetitionChallengeId.Value))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);

        var rows = competitionChallenges.Select(instance =>
        {
            var template = challenges[instance.ChallengeId];
            return new
            {
                instance.Id,
                instance.CompetitionId,
                instance.ChallengeId,
                instance.BaseScore,
                instance.Order,
                instance.IsPublished,
                instance.RulesJson,
                instance.Revision,
                instance.LastScheduledAwdRound,
                instance.AwdScheduleCompetitionRevision,
                instance.AwdScheduleChallengeRevision,
                instance.AwdScheduleDueAt,
                instance.UpdatedAt,
                instance.DeletedAt,
                Template = new
                {
                    template.Id,
                    template.OwnerId,
                    template.ManagerIds,
                    template.Mode,
                    template.Visibility,
                    template.Title,
                    template.Description,
                    template.Direction,
                    template.DefinitionJson,
                    template.Revision,
                    template.CreatedAt,
                    template.UpdatedAt,
                    template.DeletedAt
                },
                Hints = hints.Where(item => item.CompetitionChallengeId == instance.Id)
                    .Select(item => new
                    {
                        item.Id,
                        item.Content,
                        item.Cost,
                        item.PublishedAt,
                        item.PublicationRevision,
                        item.CreatedAt,
                        item.UpdatedAt,
                        item.DeletedAt
                    }),
                Attachments = attachments.Where(item => item.ChallengeId == template.Id)
                    .Select(item => new
                    {
                        item.Id,
                        item.FileName,
                        item.ContentType,
                        item.Length,
                        Sha256 = Convert.ToHexString(item.Sha256Bytes),
                        item.CreatedAt,
                        item.DeletedAt
                    }),
                Flags = flags.Where(item => item.ChallengeId == template.Id
                        || item.CompetitionChallengeId == instance.Id)
                    .Select(item => new
                    {
                        item.Id,
                        item.ChallengeId,
                        item.CompetitionChallengeId,
                        item.TeamId,
                        Sha256 = Convert.ToHexString(item.FlagSha256),
                        Flag = includeProtectedFlags ? item.Flag : null,
                        item.SpecificationKind,
                        item.SpecificationId,
                        item.ValidStart,
                        item.ValidUntil,
                        item.CreatedAt,
                        item.DeletedAt
                    })
            };
        });
        return await WriteRowsAsync(
            archive,
            "challenges.ndjson",
            rows,
            cancellationToken);
    }

    private async Task<long> WriteSubmissionsAsync(
        ZipArchive archive,
        Guid competitionId,
        bool includeProtectedFlags,
        CancellationToken cancellationToken)
    {
        var query = db.Submissions.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId)
            .GroupJoin(
                db.PatchUploads.AsNoTracking(),
                submission => submission.PatchUploadId,
                patch => patch.Id,
                (submission, patches) => new { submission, patches })
            .SelectMany(
                item => item.patches.DefaultIfEmpty(),
                (item, patch) => new { item.submission, patch })
            .OrderBy(item => item.submission.ReceivedAt)
            .ThenBy(item => item.submission.Id)
            .Select(item => new
            {
                item.submission.Id,
                item.submission.CompetitionId,
                item.submission.TeamId,
                item.submission.CompetitionChallengeId,
                item.submission.SubmittedByUserId,
                item.submission.Kind,
                item.submission.ReceivedAt,
                SubmittedFlag = includeProtectedFlags ? item.submission.SubmittedFlag : null,
                item.submission.SubmittedFlagSha256,
                item.submission.PatchUploadId,
                PatchUpload = item.patch == null
                    ? null
                    : new
                    {
                        item.patch.Id,
                        item.patch.OriginalFileName,
                        item.patch.ContentType,
                        item.patch.ByteLength,
                        item.patch.Sha256,
                        item.patch.UploadedAt,
                        item.patch.ConsumedAt
                    },
                item.submission.EvaluationState,
                item.submission.EvaluationFailureCode,
                item.submission.EvaluationUpdatedAt,
                item.submission.CurrentScoringEventId,
                item.submission.ProcessingVersion,
                item.submission.EvaluationResultBodySha256
            });
        return await WriteAsyncRowsAsync(
            archive,
            "submissions.ndjson",
            query.AsAsyncEnumerable(),
            cancellationToken);
    }

    private async Task<long> WriteCheatIncidentsAsync(
        ZipArchive archive,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var incidents = await db.ScoringEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.FailureCode == ScoringFailureCode.ForeignTeamFlagDetected)
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var ids = incidents.Select(item => item.Id).ToArray();
        var resolutions = await db.CompetitionEvents.AsNoTracking()
            .Where(item => item.ScoringEventId != null
                && ids.Contains(item.ScoringEventId.Value)
                && CheatResolutionKinds.Contains(item.Kind))
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var rows = incidents.Select(item => new
        {
            IncidentId = item.Id,
            item.SubmissionId,
            SourceTeamId = item.TeamId,
            OwnerTeamId = item.VictimTeamId,
            item.CompetitionChallengeId,
            item.Result,
            item.FailureCode,
            DetectedAt = item.OccurredAt,
            item.CreatedAt,
            item.DeletedAt,
            ResolutionHistory = resolutions
                .Where(resolution => resolution.ScoringEventId == item.Id)
                .Select(resolution => new
                {
                    resolution.Id,
                    resolution.Kind,
                    resolution.ActorUserId,
                    resolution.Reason,
                    resolution.OccurredAt
                })
        });
        return await WriteRowsAsync(
            archive,
            "cheat-incidents.ndjson",
            rows,
            cancellationToken);
    }

    private async Task<GeneratedArtifact> GeneratePlatformAuditAsync(
        DataExport job,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using var output = File.Create(temporaryPath);
        await using var limited = new LengthLimitedWriteStream(output, maximumArchiveBytes);
        DateTimeOffset? beforeOccurredAt = null;
        Guid? beforeId = null;
        while (true)
        {
            var page = await platformAudits.QueryAsync(
                new PlatformAuditQuery(
                    null,
                    null,
                    null,
                    null,
                    null,
                    beforeOccurredAt,
                    beforeId,
                    2_000),
                cancellationToken);
            if (page.Count == 0)
                break;
            foreach (var row in page)
                await WriteJsonLineAsync(limited, row, cancellationToken);
            if (page.Count < 2_000)
                break;
            var last = page[^1];
            beforeOccurredAt = last.OccurredAt;
            beforeId = last.Id;
        }
        await limited.FlushAsync(cancellationToken);
        return new(
            $"data-exports/{job.Id:N}.ndjson",
            $"platform-audit-{job.RequestedAt:yyyyMMddHHmmss}.ndjson",
            NdjsonContentType);
    }

    private async Task CompleteAsync(
        Guid dataExportId,
        StoredObject storedObject,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await db.DataExports.SingleAsync(
            item => item.Id == dataExportId,
            cancellationToken);
        if (job.Status == DataExportStatus.Expired)
        {
            await transaction.RollbackAsync(cancellationToken);
            await objectStorage.DeleteAsync(storedObject.ObjectKey, cancellationToken);
            return;
        }

        var now = timeProvider.GetUtcNow();
        job.Status = DataExportStatus.Available;
        job.ActiveSlot = null;
        job.CompletedAt = now;
        job.ExpiresAt = now.AddHours(24);
        job.ObjectKey = storedObject.ObjectKey;
        job.FileName = storedObject.FileName;
        job.ContentType = storedObject.ContentType;
        job.Length = storedObject.Length;
        job.Sha256 = storedObject.Sha256;
        await AddNotificationAsync(job, NotificationKind.DataExportReady, cancellationToken);
        if (job.IncludeProtectedFlags && job.CompetitionId is Guid competitionId)
        {
            var reason = $"data-export:{job.Id:N}; {job.Reason}";
            await competitionEvents.RecordAsync(new CompetitionEventDraft(
                competitionId,
                CompetitionEventKind.ProtectedCompetitionExportCreated,
                CompetitionEventLevel.Warning,
                CompetitionEventVisibility.Staff,
                now,
                ActorUserId: job.RequestedByUserId,
                Reason: reason[..Math.Min(reason.Length, 512)]), cancellationToken);
        }
        await outbox.ScheduleAsync(new ExpireDataExport(job.Id), job.ExpiresAt.Value);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private async Task FailAsync(
        Guid dataExportId,
        DataExportFailureCode failureCode,
        string detail,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await db.DataExports.SingleOrDefaultAsync(
            item => item.Id == dataExportId,
            cancellationToken);
        if (job is null || job.Status is DataExportStatus.Available or DataExportStatus.Expired)
            return;
        job.Status = DataExportStatus.Failed;
        job.ActiveSlot = null;
        job.CompletedAt = timeProvider.GetUtcNow();
        job.FailureCode = failureCode;
        job.FailureDetail = detail;
        await AddNotificationAsync(job, NotificationKind.DataExportFailed, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task AddNotificationAsync(
        DataExport job,
        NotificationKind kind,
        CancellationToken cancellationToken)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(
                user => user.Id == job.RequestedByUserId,
                cancellationToken))
        {
            return;
        }
        var sourceKey = $"data-export:{job.Id:N}:{kind}";
        if (await db.Notifications.AsNoTracking().AnyAsync(
                item => item.UserId == job.RequestedByUserId
                    && item.SourceEventKey == sourceKey,
                cancellationToken))
        {
            return;
        }
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(timeProvider.GetUtcNow()),
            UserId = job.RequestedByUserId,
            CompetitionId = job.CompetitionId,
            EntityId = job.Id,
            Kind = kind,
            SourceEventKey = sourceKey,
            PayloadJson = JsonSerializer.Serialize(new
            {
                ExportId = job.Id,
                job.Scope,
                job.CompetitionId,
                job.FileName,
                job.ExpiresAt,
                job.FailureCode
            }, JsonOptions),
            CreatedAt = timeProvider.GetUtcNow()
        });
    }

    private async Task TryDeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await objectStorage.DeleteAsync(objectKey, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.LogWarning(
                exception,
                "Failed to clean up data export object {ObjectKey}.",
                objectKey);
        }
    }

    private static async Task<long> WriteRowsAsync<T>(
        ZipArchive archive,
        string entryName,
        IEnumerable<T> rows,
        CancellationToken cancellationToken,
        bool appendNewline = true)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        long count = 0;
        foreach (var row in rows)
        {
            await JsonSerializer.SerializeAsync(stream, row, JsonOptions, cancellationToken);
            if (appendNewline)
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            count++;
        }
        return count;
    }

    private static async Task<long> WriteAsyncRowsAsync<T>(
        ZipArchive archive,
        string entryName,
        IAsyncEnumerable<T> rows,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        long count = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await WriteJsonLineAsync(stream, row, cancellationToken);
            count++;
        }
        return count;
    }

    private static async Task WriteJsonLineAsync<T>(
        Stream stream,
        T row,
        CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(stream, row, JsonOptions, cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }

    private sealed record GeneratedArtifact(
        string ObjectKey,
        string FileName,
        string ContentType);

    private sealed class DataExportGenerationException(
        DataExportFailureCode failureCode,
        string message,
        Exception? innerException = null) : Exception(message, innerException)
    {
        internal DataExportFailureCode FailureCode { get; } = failureCode;
    }

    private sealed class LengthLimitedWriteStream(Stream inner, long maximumLength) : Stream
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
            if (written > maximumLength - count)
            {
                throw new DataExportGenerationException(
                    DataExportFailureCode.SizeLimitExceeded,
                    "The generated export exceeded the configured size limit.");
            }
        }
    }
}
