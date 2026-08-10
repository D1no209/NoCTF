namespace NoCTF.Application.GameplayFacts.PatchUploads;

public sealed record FixArchiveDescriptor(
    string ObjectKey,
    string FileName,
    string ContentType);

public interface IFixArchiveReader
{
    Task<FixArchiveDescriptor?> FindAsync(
        Guid gameplayFactId,
        CancellationToken cancellationToken);
}
