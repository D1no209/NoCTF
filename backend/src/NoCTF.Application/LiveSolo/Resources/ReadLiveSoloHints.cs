namespace NoCTF.Application.LiveSolo.Resources;

public sealed record LiveSoloHintView(Guid Id, string Content, DateTimeOffset PublishedAt);
public interface ILiveSoloHintReader
{
    Task<IReadOnlyList<LiveSoloHintView>?> ReadAsync(LiveSoloResourceRequest request, CancellationToken ct);
}
