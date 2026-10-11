using NoCTF.Application.Storage;
using FluentStorage.Storage;

namespace NoCTF.Application.Theory.Questions;

public enum TheoryImageAudience { Editor, Participant, Reviewer }
public enum TheoryImageFailure { NotFoundOrForbidden, InvalidImage, TooLarge, Conflict }
public sealed record TheoryImageReference(Guid FileId, string ObjectKey, string ContentType);
public sealed record TheoryImageResult(Guid? FileId = null, Guid? RevisionId = null, TheoryImageFailure? Failure = null);
public interface ITheoryImageStore
{
    Task<TheoryImageResult> AttachAsync(Guid actorId, Guid questionId, Guid expectedRevisionId, Guid fileId, DateTimeOffset now, CancellationToken ct);
    Task<TheoryImageReference?> ReadAsync(Guid actorId, Guid scopeId, Guid? paperId, Guid fileId, TheoryImageAudience audience, DateTimeOffset now, CancellationToken ct);
}

public sealed class ManageTheoryImages(ITheoryQuestionBank bank, ITheoryImageStore references, ManagedFileUploads uploads, IStore objects)
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public async Task<TheoryImageResult> UploadAsync(Guid actorId, Guid questionId, Stream content, DateTimeOffset now, CancellationToken ct)
    {
        var question = await bank.ReadAsync(actorId, questionId, ct);
        if (question is null) return new(Failure: TheoryImageFailure.NotFoundOrForbidden);
        using var image = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await content.ReadAsync(buffer, ct)) > 0)
        {
            if (image.Length + count > MaximumBytes) return new(Failure: TheoryImageFailure.TooLarge);
            await image.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        var bytes = image.ToArray();
        var mime = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
            : bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? "image/jpeg"
            : bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" ? "image/webp" : null;
        if (mime is null) return new(Failure: TheoryImageFailure.InvalidImage);
        var id = Guid.CreateVersion7(now); image.Position = 0;
        await uploads.CreateAsync(id, $"theory/{questionId:N}/{id:N}", "question-image", mime, image, now, ct);
        var result = await references.AttachAsync(actorId, questionId, question.RevisionId, id, now, ct);
        if (result.Failure is not null) await uploads.AbandonAsync(id);
        return result;
    }

    public async Task<BusinessFileContent?> ReadAsync(Guid actorId, Guid scopeId, Guid? paperId, Guid fileId,
        TheoryImageAudience audience, DateTimeOffset now, CancellationToken ct)
    {
        var reference = await references.ReadAsync(actorId, scopeId, paperId, fileId, audience, now, ct);
        if (reference is null) return null;
        return new(fileId, await objects.OpenRead(reference.ObjectKey, ct), reference.ContentType, "question-image");
    }
}
