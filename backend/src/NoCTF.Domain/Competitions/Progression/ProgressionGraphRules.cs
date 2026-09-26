namespace NoCTF.Domain.Competitions.Progression;

public enum ProgressionGraphFailure : short
{
    TooManyNodes,
    TooManyEdges,
    DuplicateNode,
    DuplicateChallenge,
    InvalidPosition,
    InvalidCondition,
    MissingEndpoint,
    SelfReference,
    DuplicateEdge,
    Cycle
}

public sealed record ProgressionNodeState(Guid NodeId, bool Active, bool Complete);

public sealed record ProgressionGraphEvaluation(
    IReadOnlyDictionary<Guid, ProgressionNodeState> Nodes,
    IReadOnlySet<Guid> ActiveBadgeIds);

public static class ProgressionGraphRules
{
    public const int MaximumNodes = 512;
    public const int MaximumEdges = 4096;
    public const double MaximumCoordinate = 100_000;

    public static ProgressionGraphFailure? Validate(
        IReadOnlyCollection<ProgressionNode> nodes,
        IReadOnlyCollection<ProgressionEdge> edges)
    {
        if (nodes.Count > MaximumNodes) return ProgressionGraphFailure.TooManyNodes;
        if (edges.Count > MaximumEdges) return ProgressionGraphFailure.TooManyEdges;
        if (nodes.Select(node => node.Id).Distinct().Count() != nodes.Count)
            return ProgressionGraphFailure.DuplicateNode;
        if (nodes.OfType<ChallengeProgressionNode>()
                .Select(node => node.CompetitionChallengeId).Distinct().Count()
            != nodes.Count(node => node is ChallengeProgressionNode))
            return ProgressionGraphFailure.DuplicateChallenge;
        if (nodes.Any(node => !double.IsFinite(node.PositionX)
            || !double.IsFinite(node.PositionY)
            || Math.Abs(node.PositionX) > MaximumCoordinate
            || Math.Abs(node.PositionY) > MaximumCoordinate))
            return ProgressionGraphFailure.InvalidPosition;

        var nodeIds = nodes.Select(node => node.Id).ToHashSet();
        if (edges.Any(edge => !Enum.IsDefined(edge.Condition)))
            return ProgressionGraphFailure.InvalidCondition;
        if (edges.Any(edge => !nodeIds.Contains(edge.SourceNodeId)
            || !nodeIds.Contains(edge.TargetNodeId)))
            return ProgressionGraphFailure.MissingEndpoint;
        if (edges.Any(edge => edge.SourceNodeId == edge.TargetNodeId))
            return ProgressionGraphFailure.SelfReference;
        if (edges.Select(edge => (edge.SourceNodeId, edge.TargetNodeId))
                .Distinct().Count() != edges.Count)
            return ProgressionGraphFailure.DuplicateEdge;
        return TopologicalOrder(nodes, edges) is null
            ? ProgressionGraphFailure.Cycle : null;
    }

    public static ProgressionGraphEvaluation Evaluate(
        IReadOnlyCollection<ProgressionNode> nodes,
        IReadOnlyCollection<ProgressionEdge> edges,
        IReadOnlySet<Guid> completedChallengeIds)
    {
        var failure = Validate(nodes, edges);
        if (failure is not null)
            throw new ArgumentException($"Invalid progression graph: {failure}.", nameof(nodes));

        var order = TopologicalOrder(nodes, edges)!;
        var incoming = edges.GroupBy(edge => edge.TargetNodeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var states = new Dictionary<Guid, ProgressionNodeState>(nodes.Count);
        var activeBadges = new HashSet<Guid>();
        foreach (var node in order)
        {
            var active = !incoming.TryGetValue(node.Id, out var requirements)
                || requirements.All(edge => edge.Condition == ProgressionPrerequisiteCondition.Completed
                    ? states[edge.SourceNodeId].Complete
                    : !states[edge.SourceNodeId].Complete);
            var complete = node switch
            {
                ChallengeProgressionNode challenge =>
                    completedChallengeIds.Contains(challenge.CompetitionChallengeId),
                BadgeProgressionNode => active,
                _ => throw new InvalidOperationException("Unsupported progression node type.")
            };
            states.Add(node.Id, new(node.Id, active, complete));
            if (active && node is BadgeProgressionNode badge)
                activeBadges.Add(badge.CompetitionBadgeId);
        }
        return new(states, activeBadges);
    }

    private static ProgressionNode[]? TopologicalOrder(
        IReadOnlyCollection<ProgressionNode> nodes,
        IReadOnlyCollection<ProgressionEdge> edges)
    {
        var byId = nodes.ToDictionary(node => node.Id);
        var inDegree = nodes.ToDictionary(node => node.Id, _ => 0);
        var outgoing = nodes.ToDictionary(node => node.Id, _ => new List<Guid>());
        foreach (var edge in edges)
        {
            if (!outgoing.TryGetValue(edge.SourceNodeId, out var targets)
                || !inDegree.ContainsKey(edge.TargetNodeId))
                return null;
            targets.Add(edge.TargetNodeId);
            inDegree[edge.TargetNodeId]++;
        }
        var ready = new Queue<Guid>(inDegree.Where(pair => pair.Value == 0)
            .Select(pair => pair.Key));
        var result = new List<ProgressionNode>(nodes.Count);
        while (ready.TryDequeue(out var id))
        {
            result.Add(byId[id]);
            foreach (var target in outgoing[id])
                if (--inDegree[target] == 0) ready.Enqueue(target);
        }
        return result.Count == nodes.Count ? result.ToArray() : null;
    }
}
