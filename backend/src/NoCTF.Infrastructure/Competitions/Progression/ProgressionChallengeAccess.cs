using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Progression;

public sealed class ProgressionChallengeAccess(
    NoCtfDbContext db, ProgressionGraphReadCache? graphs = null)
    : IProgressionChallengeAccess
{
    public Task<IReadOnlyDictionary<Guid, ProgressionChallengeStatus>>
        ReadStatusesAsync(Guid competitionId, Guid? teamId, CancellationToken ct) =>
        ReadStatusesCoreAsync(competitionId, teamId, allowCache: true, ct);

    private async Task<IReadOnlyDictionary<Guid, ProgressionChallengeStatus>>
        ReadStatusesCoreAsync(Guid competitionId, Guid? teamId, bool allowCache,
            CancellationToken ct)
    {
        var graph = allowCache && graphs is not null
            ? await graphs.ReadAsync(db, competitionId, ct)
            : await db.CompetitionProgressions.AsNoTracking()
                .Include(item => item.Nodes).Include(item => item.Edges)
                .AsSplitQuery().Where(item => item.CompetitionId == competitionId)
                .Select(item => item).SingleOrDefaultAsync(ct) is { } loaded
                ? ProgressionGraphSnapshot.From(loaded) : null;
        if (graph is not { Enabled: true } || graph.Nodes.Length == 0)
            return new Dictionary<Guid, ProgressionChallengeStatus>();
        var challengeIds = graph.Nodes
            .Where(node => node.Kind == ProgressionNodeKind.Challenge)
            .Select(node => node.ResourceId).ToArray();
        var completed = teamId is not Guid id
            ? new HashSet<Guid>()
            : (await db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.CompetitionId == competitionId
                    && fact.TeamId == id
                    && challengeIds.Contains(fact.CompetitionChallengeId)
                    && (fact.Kind == GameplayFactKind.FlagAttempt
                        || fact.Kind == GameplayFactKind.FixAttempt)
                    && fact.State == GameplayFactState.Completed
                    && fact.Result == GameplayFactResult.Correct)
                .Select(fact => fact.CompetitionChallengeId)
                .Distinct().ToArrayAsync(ct)).ToHashSet();
        var evaluation = ProgressionGraphRules.Evaluate(
            graph.ToDomainNodes(), graph.ToDomainEdges(), completed);
        var incoming = graph.Edges.GroupBy(edge => edge.TargetNodeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        return graph.Nodes.Where(node => node.Kind == ProgressionNodeKind.Challenge)
            .ToDictionary(
            node => node.ResourceId,
            node =>
            {
                var edges = incoming.GetValueOrDefault(node.Id) ?? [];
                var satisfied = edges.Count(edge =>
                    edge.Condition == ProgressionPrerequisiteCondition.Completed
                        ? evaluation.Nodes[edge.SourceNodeId].Complete
                        : !evaluation.Nodes[edge.SourceNodeId].Complete);
                return new ProgressionChallengeStatus(
                    evaluation.Nodes[node.Id].Active, satisfied, edges.Length);
            });
    }

    public async Task<bool> IsActiveAsync(
        Guid competitionId, Guid competitionChallengeId, Guid? teamId,
        CancellationToken ct)
    {
        var graph = await db.CompetitionProgressions.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId && item.Enabled)
            .Select(item => new
            {
                item.Revision,
                NodeId = item.Nodes.OfType<ChallengeProgressionNode>()
                    .Where(node => node.CompetitionChallengeId == competitionChallengeId)
                    .Select(node => (Guid?)node.Id)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(ct);
        if (graph?.NodeId is not Guid nodeId)
            return true;

        if (teamId is Guid id)
        {
            var current = await db.TeamProgressionNodeStates.AsNoTracking()
                .Where(state => state.TeamId == id && state.NodeId == nodeId
                    && state.GraphRevision == graph.Revision)
                .Select(state => (bool?)state.Active)
                .SingleOrDefaultAsync(ct);
            if (current is bool active)
                return active;
        }

        // A new team or an interrupted reconciliation has no current state yet.
        // Compute from the relational facts rather than trusting a stale cache entry.
        var statuses = await ReadStatusesCoreAsync(
            competitionId, teamId, allowCache: false, ct);
        return !statuses.TryGetValue(competitionChallengeId, out var status) || status.Active;
    }
}
