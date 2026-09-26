using FluentStorage.Storage;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Competitions.Progression;

public sealed record CompetitionBadgeView(
    Guid Id, Guid CompetitionId, string Name, string? Description,
    Guid ImageFileId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public enum CompetitionBadgeFailure : short
{
    NotFound,
    InUse,
    InvalidImage,
    InvalidName,
    ConcurrencyConflict
}

public sealed record CompetitionBadgeResult(
    CompetitionBadgeView? Badge, CompetitionBadgeFailure? Failure = null);

public interface ICompetitionBadgeStore
{
    Task<IReadOnlyList<CompetitionBadgeView>?> ListAsync(
        Guid competitionId, CancellationToken ct);
    Task<CompetitionBadgeResult> CreateAsync(
        Guid competitionId, Guid badgeId, string name, string? description,
        Guid imageFileId, DateTimeOffset now, CancellationToken ct);
    Task<CompetitionBadgeResult> ReplaceAsync(
        Guid competitionId, Guid badgeId, string? name, string? description,
        Guid? imageFileId, DateTimeOffset now, CancellationToken ct);
    Task<CompetitionBadgeFailure?> DeleteAsync(
        Guid competitionId, Guid badgeId, DateTimeOffset now, CancellationToken ct);
    Task<BusinessFileReference?> GetImageAsync(
        Guid competitionId, Guid badgeId, CancellationToken ct);
}

public sealed class ManageCompetitionBadges(
    ICompetitionBadgeStore store,
    ManagedFileUploads uploads,
    IStore objects)
{
    public Task<IReadOnlyList<CompetitionBadgeView>?> ListAsync(
        Guid competitionId, CancellationToken ct) => store.ListAsync(competitionId, ct);

    public async Task<CompetitionBadgeResult> CreateAsync(
        Guid competitionId, string name, string? description,
        string fileName, string contentType, Stream content,
        DateTimeOffset now, CancellationToken ct)
    {
        var badgeId = Guid.CreateVersion7(now);
        var fileId = Guid.CreateVersion7(now);
        var uploaded = await uploads.CreateAsync(fileId,
            $"competitions/{competitionId:N}/badges/{badgeId:N}/{fileId:N}",
            Path.GetFileName(fileName), contentType, content, now, ct);
        var attached = false;
        try
        {
            var result = await store.CreateAsync(
                competitionId, badgeId, name, description, uploaded.FileId, now, ct);
            attached = result.Badge is not null;
            return result;
        }
        finally
        {
            if (!attached) await uploads.AbandonAsync(fileId);
        }
    }

    public async Task<CompetitionBadgeResult> ReplaceAsync(
        Guid competitionId, Guid badgeId, string? name, string? description,
        string? fileName, string? contentType, Stream? content,
        DateTimeOffset now, CancellationToken ct)
    {
        Guid? fileId = null;
        if (content is not null)
        {
            fileId = Guid.CreateVersion7(now);
            await uploads.CreateAsync(fileId.Value,
                $"competitions/{competitionId:N}/badges/{badgeId:N}/{fileId:N}",
                Path.GetFileName(fileName!), contentType!, content, now, ct);
        }
        var attached = false;
        try
        {
            var result = await store.ReplaceAsync(
                competitionId, badgeId, name, description, fileId, now, ct);
            attached = result.Badge is not null;
            return result;
        }
        finally
        {
            if (!attached && fileId is { } abandoned)
                await uploads.AbandonAsync(abandoned);
        }
    }

    public Task<CompetitionBadgeFailure?> DeleteAsync(
        Guid competitionId, Guid badgeId, DateTimeOffset now, CancellationToken ct) =>
        store.DeleteAsync(competitionId, badgeId, now, ct);

    public async Task<BusinessFileContent?> OpenImageAsync(
        Guid competitionId, Guid badgeId, CancellationToken ct)
    {
        var file = await store.GetImageAsync(competitionId, badgeId, ct);
        if (file is null) return null;
        var content = await objects.OpenRead(file.ObjectKey, ct);
        return content is null
            ? null : new(file.FileId, content, file.ContentType, file.FileName);
    }
}
