using NoCTF.Application.Teams.Moderation;

namespace NoCTF.Application.GameplayFacts.PatchUploads;

public enum AdminPatchFailure
{
    Forbidden, SubmissionNotFound, NotFixSubmission, PatchNotFound,
    InvalidAssociation, FileNotFound, StorageUnavailable, AuditUnavailable
}

public sealed record AdminPatchMetadata(Guid FileId, string FileName, long ByteLength, DateTimeOffset UploadedAt, string Sha256);
public sealed record AdminPatchMetadataResult(AdminPatchFailure? Failure = null, AdminPatchMetadata? Metadata = null);
public sealed record AdminPatchDownloadResult(AdminPatchFailure? Failure = null, Stream? Content = null, AdminPatchMetadata? Metadata = null);

public interface IAdminPatchDownloadStore
{
    Task<AdminPatchMetadataResult> DescribeAsync(Guid competitionId, Guid gameplayFactId, CancellationToken ct);
    Task<AdminPatchDownloadResult> OpenAsync(Guid competitionId, Guid gameplayFactId, Guid actorId, DateTimeOffset now, CancellationToken ct);
}

public sealed class AccessAdminPatch(IAdminPatchDownloadStore store, ICompetitionModerationAuthorizer authorizer)
{
    public async Task<AdminPatchMetadataResult> DescribeAsync(Guid competitionId, Guid gameplayFactId, Guid actorId, CancellationToken ct) =>
        await authorizer.CanJudgeAsync(actorId, competitionId, ct)
            ? await store.DescribeAsync(competitionId, gameplayFactId, ct)
            : new(AdminPatchFailure.Forbidden);

    public async Task<AdminPatchDownloadResult> DownloadAsync(Guid competitionId, Guid gameplayFactId, Guid actorId, DateTimeOffset now, CancellationToken ct) =>
        await authorizer.CanJudgeAsync(actorId, competitionId, ct)
            ? await store.OpenAsync(competitionId, gameplayFactId, actorId, now, ct)
            : new(AdminPatchFailure.Forbidden);
}
