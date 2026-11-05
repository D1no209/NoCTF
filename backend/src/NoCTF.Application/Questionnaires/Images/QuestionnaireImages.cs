using FluentStorage.Storage;
using MetadataExtractor.Util;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Questionnaires.Images;

public enum QuestionnaireImageAudience { Editor, Participant, Reviewer }
public enum QuestionnaireImageFailure { NotFoundOrForbidden, InvalidImage, TooLarge, Conflict }
public sealed record QuestionnaireImageReference(Guid FileId, string ObjectKey, string ContentType);
public sealed record QuestionnaireImageResult(Guid? FileId = null, Guid? RevisionId = null, QuestionnaireImageFailure? Failure = null);

public interface IQuestionnaireImageStore
{
    Task<Guid?> FindEditableRevisionAsync(Guid actorId, Guid challengeId, Guid questionId, CancellationToken ct);
    Task<QuestionnaireImageResult> AttachAsync(Guid actorId, Guid challengeId, Guid questionId, Guid expectedRevisionId,
        Guid fileId, DateTimeOffset now, CancellationToken ct);
    Task<QuestionnaireImageReference?> ReadAsync(Guid actorId, Guid scopeId, Guid? sessionId, Guid fileId,
        QuestionnaireImageAudience audience, DateTimeOffset now, CancellationToken ct);
}

public sealed class ManageQuestionnaireImages(IQuestionnaireImageStore references, ManagedFileUploads uploads, IStore objects)
{
    public const int MaximumBytes = 5 * 1024 * 1024;

    public async Task<QuestionnaireImageResult> UploadAsync(Guid actorId, Guid challengeId, Guid questionId,
        Stream content, DateTimeOffset now, CancellationToken ct)
    {
        var revision = await references.FindEditableRevisionAsync(actorId, challengeId, questionId, ct);
        if (revision is null) return new(Failure: QuestionnaireImageFailure.NotFoundOrForbidden);
        using var image = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await content.ReadAsync(buffer, ct)) > 0)
        {
            if (image.Length + count > MaximumBytes) return new(Failure: QuestionnaireImageFailure.TooLarge);
            await image.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        if (image.Length < 16) return new(Failure: QuestionnaireImageFailure.InvalidImage);
        image.Position = 0;
        var mime = FileTypeDetector.DetectFileType(image) switch
        {
            FileType.Png => "image/png", FileType.Jpeg => "image/jpeg", FileType.WebP => "image/webp", _ => null
        };
        if (mime is null) return new(Failure: QuestionnaireImageFailure.InvalidImage);
        var id = Guid.CreateVersion7(now);
        image.Position = 0;
        await uploads.CreateAsync(id, $"questionnaire/{challengeId:N}/{id:N}", "question-image", mime, image, now, ct);
        var result = await references.AttachAsync(actorId, challengeId, questionId, revision.Value, id, now, ct);
        if (result.Failure is not null) await uploads.AbandonAsync(id);
        return result;
    }

    public async Task<BusinessFileContent?> ReadAsync(Guid actorId, Guid scopeId, Guid? sessionId, Guid fileId,
        QuestionnaireImageAudience audience, DateTimeOffset now, CancellationToken ct)
    {
        var reference = await references.ReadAsync(actorId, scopeId, sessionId, fileId, audience, now, ct);
        return reference is null ? null : new(fileId, await objects.OpenRead(reference.ObjectKey, ct), reference.ContentType, "question-image");
    }
}
