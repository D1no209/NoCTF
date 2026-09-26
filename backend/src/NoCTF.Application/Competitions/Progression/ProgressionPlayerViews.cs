using NoCTF.Domain.Competitions.Progression;

namespace NoCTF.Application.Competitions.Progression;

public sealed record ProgressionBadgeDisplay(
    Guid Id, Guid CompetitionId, string CompetitionTitle,
    string Name, string? Description, Guid ImageFileId);

public sealed record PlayerProgressionNode(
    Guid Id, ProgressionNodeKind Kind, Guid ResourceId,
    string Title, string? Direction, double PositionX, double PositionY,
    bool Active, bool Complete, Guid? ImageFileId);

public sealed record PlayerProgressionMap(
    bool Enabled, bool ShowPlayerMap, long Revision,
    IReadOnlyList<PlayerProgressionNode> Nodes,
    IReadOnlyList<ProgressionEdgeDraft> Edges,
    IReadOnlyList<ProgressionBadgeDisplay> Badges);

public interface IProgressionPlayerReader
{
    Task<PlayerProgressionMap> ReadAsync(
        Guid competitionId, Guid? teamId, CancellationToken ct);
    Task<IReadOnlyList<ProgressionBadgeDisplay>> ReadPublicUserBadgesAsync(
        Guid userId, CancellationToken ct);
}
