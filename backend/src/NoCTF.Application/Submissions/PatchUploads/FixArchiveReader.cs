namespace NoCTF.Application.Submissions.PatchUploads;

public sealed record FixArchiveDescriptor(
    string ObjectKey,
    string FileName,
    string ContentType);

public interface IFixArchiveReader
{
    Task<FixArchiveDescriptor?> FindAsync(
        Guid submissionId,
        CancellationToken cancellationToken);
}
