using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Competitions.Progression;

/// <summary>Reusable graph structure only; team activation is never authorized from this cache.</summary>
public sealed class ProgressionGraphReadCache(IFusionCacheProvider caches)
{
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.ReadModels);

    public async Task<ProgressionGraphSnapshot?> ReadAsync(
        NoCtfDbContext db, Guid competitionId, CancellationToken ct) =>
        await cache.GetOrSetAsync<ProgressionGraphSnapshot?>(
            Key(competitionId),
            async (_, token) =>
            {
                var graph = await db.CompetitionProgressions.AsNoTracking()
                    .Include(item => item.Nodes).Include(item => item.Edges)
                    .AsSplitQuery().SingleOrDefaultAsync(item =>
                        item.CompetitionId == competitionId, token);
                return graph is null ? null : ProgressionGraphSnapshot.From(graph);
            }, token: ct);

    public Task InvalidateAsync(Guid competitionId, CancellationToken ct) =>
        cache.RemoveAsync(Key(competitionId), token: ct).AsTask();

    public async Task<CompetitionBadgeView[]> ReadBadgesAsync(
        NoCtfDbContext db, Guid competitionId, CancellationToken ct) =>
        await cache.GetOrSetAsync<CompetitionBadgeView[]>(
            BadgeKey(competitionId),
            async (_, token) => await db.CompetitionBadges.AsNoTracking()
                .Where(item => item.CompetitionId == competitionId && item.DeletedAt == null)
                .OrderBy(item => item.Name).ThenBy(item => item.Id)
                .Select(item => new CompetitionBadgeView(
                    item.Id, item.CompetitionId, item.Name, item.Description,
                    item.ImageFileId, item.CreatedAt, item.UpdatedAt))
                .ToArrayAsync(token), token: ct);

    public Task InvalidateBadgesAsync(Guid competitionId, CancellationToken ct) =>
        cache.RemoveAsync(BadgeKey(competitionId), token: ct).AsTask();

    private static string Key(Guid competitionId) =>
        $"progression:graph:{competitionId:N}";

    private static string BadgeKey(Guid competitionId) =>
        $"progression:badges:{competitionId:N}";
}

public sealed record ProgressionGraphSnapshot(
    bool Enabled,
    bool ShowPlayerMap,
    long Revision,
    ProgressionGraphNodeSnapshot[] Nodes,
    ProgressionGraphEdgeSnapshot[] Edges)
{
    public static ProgressionGraphSnapshot From(CompetitionProgression graph) => new(
        graph.Enabled, graph.ShowPlayerMap, graph.Revision,
        graph.Nodes.Select(node => node switch
        {
            ChallengeProgressionNode challenge => new ProgressionGraphNodeSnapshot(
                node.Id, node.Kind, challenge.CompetitionChallengeId,
                node.PositionX, node.PositionY),
            BadgeProgressionNode badge => new ProgressionGraphNodeSnapshot(
                node.Id, node.Kind, badge.CompetitionBadgeId,
                node.PositionX, node.PositionY),
            _ => throw new InvalidOperationException("Unsupported progression node.")
        }).ToArray(),
        graph.Edges.Select(edge => new ProgressionGraphEdgeSnapshot(
            edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.Condition)).ToArray());

    public ProgressionNode[] ToDomainNodes() => Nodes.Select(node => node.Kind switch
    {
        ProgressionNodeKind.Challenge => (ProgressionNode)new ChallengeProgressionNode
        {
            Id = node.Id, CompetitionChallengeId = node.ResourceId,
            PositionX = node.PositionX, PositionY = node.PositionY
        },
        ProgressionNodeKind.Badge => new BadgeProgressionNode
        {
            Id = node.Id, CompetitionBadgeId = node.ResourceId,
            PositionX = node.PositionX, PositionY = node.PositionY
        },
        _ => throw new InvalidOperationException("Unsupported progression node kind.")
    }).ToArray();

    public ProgressionEdge[] ToDomainEdges() => Edges.Select(edge => new ProgressionEdge
    {
        Id = edge.Id, SourceNodeId = edge.SourceNodeId,
        TargetNodeId = edge.TargetNodeId, Condition = edge.Condition
    }).ToArray();
}

public sealed record ProgressionGraphNodeSnapshot(
    Guid Id, ProgressionNodeKind Kind, Guid ResourceId,
    double PositionX, double PositionY);

public sealed record ProgressionGraphEdgeSnapshot(
    Guid Id, Guid SourceNodeId, Guid TargetNodeId,
    ProgressionPrerequisiteCondition Condition);
