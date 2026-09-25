using System.Text.Json;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.PatchUploads;

public sealed class AdminPatchDownloadStore(NoCtfDbContext db, IStore objects, ILogger<AdminPatchDownloadStore> logger)
    : IAdminPatchDownloadStore
{
    public async Task<AdminPatchMetadataResult> DescribeAsync(Guid competitionId, Guid gameplayFactId, CancellationToken ct)
    {
        var lookup = await FindAsync(competitionId, gameplayFactId, ct);
        return new(lookup.Failure, lookup.Metadata);
    }

    public async Task<AdminPatchDownloadResult> OpenAsync(Guid competitionId, Guid gameplayFactId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        var lookup = await FindAsync(competitionId, gameplayFactId, ct);
        if (lookup.Failure is { } failure) return new(failure);
        Stream? content;
        try
        {
            if (!await objects.ObjectExists(lookup.ObjectKey!, ct)) return new(AdminPatchFailure.FileNotFound);
            content = await objects.OpenRead(lookup.ObjectKey!, ct);
            if (content is null) return new(AdminPatchFailure.FileNotFound);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(AdminPatchFailure.FileNotFound);
        }
        catch (Amazon.S3.AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new(AdminPatchFailure.FileNotFound);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Patch file {FileId} could not be opened ({ErrorType}).", lookup.Metadata!.FileId, exception.GetType().Name);
            return new(AdminPatchFailure.StorageUnavailable);
        }

        try
        {
            // Append audit before any bytes leave the API, including for platform administrators.
            // This is an access audit, not a public competition broadcast or a scoring event.
            db.CompetitionEvents.Add(new GameplayFactPatchDownloadedEvent
            {
                Id = Guid.CreateVersion7(now), CompetitionId = competitionId,
                Level = CompetitionEventLevel.Information, Visibility = CompetitionEventVisibility.Staff,
                ActorUserId = actorId, SubjectType = EntityReferenceKind.GameplayFact, SubjectId = gameplayFactId,
                RelatedType = EntityReferenceKind.File, RelatedId = lookup.Metadata!.FileId, OccurredAt = now,
                TeamId = lookup.TeamId,
                GameplayFactId = gameplayFactId,
                CompetitionChallengeId = lookup.CompetitionChallengeId
            });
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await content.DisposeAsync();
            throw;
        }
        catch (Exception exception)
        {
            await content.DisposeAsync();
            logger.LogError("Patch access audit failed for fact {GameplayFactId}, actor {ActorId} ({ErrorType}); no file was returned.",
                gameplayFactId, actorId, exception.GetType().Name);
            return new(AdminPatchFailure.AuditUnavailable);
        }
        return new(Content: content, Metadata: lookup.Metadata);
    }

    private async Task<Lookup> FindAsync(Guid competitionId, Guid gameplayFactId, CancellationToken ct)
    {
        var fact = await db.GameplayFacts.AsNoTracking()
            .Where(x => x.Id == gameplayFactId && x.CompetitionId == competitionId)
            .Select(x => new { x.Id, x.Kind, x.ReferenceKind, x.ReferenceId, x.CompetitionChallengeId, x.TeamId, x.ActorUserId })
            .SingleOrDefaultAsync(ct);
        if (fact is null) return new(AdminPatchFailure.SubmissionNotFound);
        if (fact.Kind != GameplayFactKind.FixAttempt) return new(AdminPatchFailure.NotFixSubmission);
        if (fact.ReferenceKind != GameplayFactReferenceKind.PatchUpload || fact.ReferenceId is null)
            return new(AdminPatchFailure.PatchNotFound);
        var upload = await db.PatchUploads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fact.ReferenceId, ct);
        if (upload is null) return new(AdminPatchFailure.PatchNotFound);
        if (upload.CompetitionId != competitionId || upload.CompetitionChallengeId != fact.CompetitionChallengeId
            || upload.TeamId != fact.TeamId || upload.UploadedByUserId != fact.ActorUserId
            || !await db.CompetitionChallenges.IgnoreQueryFilters().AnyAsync(x => x.Id == fact.CompetitionChallengeId && x.CompetitionId == competitionId, ct)
            || !await db.Teams.IgnoreQueryFilters().AnyAsync(x => x.Id == fact.TeamId && x.CompetitionId == competitionId, ct))
            return new(AdminPatchFailure.InvalidAssociation);
        var file = await db.Files.AsNoTracking().SingleOrDefaultAsync(x => x.Id == upload.FileId, ct);
        if (file is null) return new(AdminPatchFailure.FileNotFound);
        return new(Metadata: new(file.Id, file.FileName, file.ByteLength, upload.UploadedAt, Convert.ToHexString(file.Sha256)),
            ObjectKey: file.ObjectKey, TeamId: upload.TeamId, CompetitionChallengeId: upload.CompetitionChallengeId);
    }

    private sealed record Lookup(AdminPatchFailure? Failure = null, AdminPatchMetadata? Metadata = null,
        string? ObjectKey = null, Guid? TeamId = null, Guid? CompetitionChallengeId = null);
}
