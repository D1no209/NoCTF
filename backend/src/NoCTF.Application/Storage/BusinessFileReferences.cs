namespace NoCTF.Application.Storage;

public enum BusinessFileReferenceState
{
    Updated,
    Cleared,
    NotFound,
    Forbidden
}

public sealed record BusinessFileReference(
    Guid FileId,
    string ObjectKey,
    string FileName,
    string ContentType);

public sealed record BusinessFileReferenceResult(
    BusinessFileReferenceState State,
    BusinessFileReference? File = null);

public interface IBusinessFileReferenceStore
{
    Task<BusinessFileReferenceResult> ReplaceTeamAvatarAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        Guid teamId,
        Guid fileId,
        StoredObject stored,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<BusinessFileReferenceResult> ClearTeamAvatarAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken);

    Task<BusinessFileReference?> GetTeamAvatarAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken);

    Task<BusinessFileReferenceResult> ReplaceCompetitionPosterAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        Guid fileId,
        StoredObject stored,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<BusinessFileReferenceResult> ClearCompetitionPosterAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        CancellationToken cancellationToken);

    Task<BusinessFileReference?> GetCompetitionPosterAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}

public sealed record BusinessFileContent(Stream Content, string ContentType, string FileName);

public sealed class ManageBusinessImages(
    IBusinessFileReferenceStore references,
    IObjectStorage objects)
{
    public async Task<BusinessFileReferenceResult> ReplaceTeamAvatarAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        Guid teamId,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var fileId = Guid.CreateVersion7(now);
        var stored = await objects.PutAsync(
            $"teams/{teamId:N}/avatars/{fileId:N}",
            NormalizeFileName(fileName),
            NormalizeContentType(contentType),
            content,
            ct);
        var result = await references.ReplaceTeamAvatarAsync(
            actorUserId, isAdministrator, competitionId, teamId, fileId, stored, now, ct);
        if (result.State != BusinessFileReferenceState.Updated)
            await TryDeleteAsync(stored.ObjectKey);
        return result;
    }

    public Task<BusinessFileReferenceResult> ClearTeamAvatarAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        Guid teamId,
        CancellationToken ct = default) =>
        references.ClearTeamAvatarAsync(
            actorUserId, isAdministrator, competitionId, teamId, ct);

    public async Task<BusinessFileContent?> GetTeamAvatarAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct = default) =>
        await references.GetTeamAvatarAsync(competitionId, teamId, ct) is { } file
            ? new(await objects.OpenReadAsync(file.ObjectKey, ct), file.ContentType, file.FileName)
            : null;

    public async Task<BusinessFileReferenceResult> ReplaceCompetitionPosterAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var fileId = Guid.CreateVersion7(now);
        var stored = await objects.PutAsync(
            $"competitions/{competitionId:N}/posters/{fileId:N}",
            NormalizeFileName(fileName),
            NormalizeContentType(contentType),
            content,
            ct);
        var result = await references.ReplaceCompetitionPosterAsync(
            actorUserId, isAdministrator, competitionId, fileId, stored, now, ct);
        if (result.State != BusinessFileReferenceState.Updated)
            await TryDeleteAsync(stored.ObjectKey);
        return result;
    }

    public Task<BusinessFileReferenceResult> ClearCompetitionPosterAsync(
        Guid actorUserId,
        bool isAdministrator,
        Guid competitionId,
        CancellationToken ct = default) =>
        references.ClearCompetitionPosterAsync(actorUserId, isAdministrator, competitionId, ct);

    public async Task<BusinessFileContent?> GetCompetitionPosterAsync(
        Guid competitionId,
        CancellationToken ct = default) =>
        await references.GetCompetitionPosterAsync(competitionId, ct) is { } file
            ? new(await objects.OpenReadAsync(file.ObjectKey, ct), file.ContentType, file.FileName)
            : null;

    private async Task TryDeleteAsync(string objectKey)
    {
        try
        {
            await objects.DeleteAsync(objectKey, CancellationToken.None);
        }
        catch
        {
            // The object has no database reference; bucket lifecycle cleanup is the final fallback.
        }
    }

    private static string NormalizeFileName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "image" : value.Trim()[..Math.Min(value.Trim().Length, 260)];

    private static string NormalizeContentType(string value) =>
        string.IsNullOrWhiteSpace(value) ? "application/octet-stream" : value.Trim();
}
