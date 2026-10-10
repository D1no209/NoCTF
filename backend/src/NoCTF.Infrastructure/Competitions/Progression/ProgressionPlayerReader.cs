using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Progression;

public sealed class ProgressionPlayerReader(
    NoCtfDbContext db, ProgressionGraphReadCache? graphs = null)
    : IProgressionPlayerReader
{
    public async Task<PlayerProgressionMap> ReadAsync(
        Guid competitionId, Guid? teamId, CancellationToken ct)
    {
        var graph = graphs is not null
            ? await graphs.ReadAsync(db, competitionId, ct)
            : await ReadGraphAsync(competitionId, ct);
        var badgeIds = teamId is not Guid id ? [] : await db.TeamProgressionBadgeStates
            .AsNoTracking().Where(item => item.TeamId == id
                && item.CompetitionId == competitionId && item.Active)
            .Select(item => item.BadgeId).ToArrayAsync(ct);
        var badges = await db.CompetitionBadges.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.DeletedAt == null && badgeIds.Contains(item.Id))
            .Join(db.Competitions.AsNoTracking(), badge => badge.CompetitionId,
                competition => competition.Id,
                (badge, competition) => new ProgressionBadgeDisplay(
                    badge.Id, badge.CompetitionId, competition.Title,
                    badge.Name, badge.Description, badge.ImageFileId))
            .ToArrayAsync(ct);
        if (graph is not { Enabled: true, ShowPlayerMap: true })
            return new(graph?.Enabled ?? false, false, graph?.Revision ?? 0,
                [], [], badges);

        var challengeIds = graph.Nodes
            .Where(node => node.Kind == ProgressionNodeKind.Challenge)
            .Select(node => node.ResourceId).ToArray();
        var completed = teamId is not Guid team
            ? new HashSet<Guid>()
            : (await db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.CompetitionId == competitionId
                    && fact.TeamId == team
                    && challengeIds.Contains(fact.CompetitionChallengeId)
                    && (fact.Kind == GameplayFactKind.FlagAttempt
                        || fact.Kind == GameplayFactKind.FixAttempt)
                    && fact.State == GameplayFactState.Completed
                    && fact.TimeEligibility == NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid
                    && (fact.Result == GameplayFactResult.Correct || fact.Result == GameplayFactResult.RightButDue))
                .Select(fact => fact.CompetitionChallengeId)
                .Distinct().ToArrayAsync(ct)).ToHashSet();
        var visited = teamId is not Guid visitTeam
            ? new Dictionary<Guid, DateTimeOffset>()
            : (await db.TeamProgressionNodeVisits.AsNoTracking()
                .Where(visit => visit.TeamId == visitTeam
                    && visit.CompetitionId == competitionId)
                .Select(visit => new { visit.NodeId, visit.FirstOpenedAt })
                .ToArrayAsync(ct)).ToDictionary(visit => visit.NodeId,
                    visit => visit.FirstOpenedAt);
        var evaluation = ProgressionGraphRules.Evaluate(
            graph.ToDomainNodes(), graph.ToDomainEdges(), completed);
        var challengeDetails = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            .Where(item => challengeIds.Contains(item.Id))
            .Join(db.Challenges.IgnoreQueryFilters().AsNoTracking(), item => item.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new
                {
                    item.Id,
                    Title = item.CustomTitle ?? challenge.Title,
                    Direction = item.Direction != null ? item.Direction.Name : challenge.Direction
                })
            .ToDictionaryAsync(item => item.Id, ct);
        var allBadgeIds = graph.Nodes
            .Where(node => node.Kind == ProgressionNodeKind.Badge)
            .Select(node => node.ResourceId).ToArray();
        var badgeDetails = await db.CompetitionBadges.AsNoTracking()
            .Where(item => allBadgeIds.Contains(item.Id) && item.DeletedAt == null)
            .Select(item => new { item.Id, item.Name, item.Description, item.ImageFileId })
            .ToDictionaryAsync(item => item.Id, ct);
        var nodes = graph.Nodes.Select(node =>
        {
            var state = evaluation.Nodes[node.Id];
            return node.Kind switch
            {
                ProgressionNodeKind.Challenge => new PlayerProgressionNode(
                    node.Id, node.Kind, node.ResourceId,
                    challengeDetails.GetValueOrDefault(node.ResourceId)?.Title
                        ?? string.Empty,
                    null, challengeDetails.GetValueOrDefault(node.ResourceId)?.Direction,
                    state.Active, state.Complete, visited.ContainsKey(node.Id),
                    node.RequiresPrerequisites,
                    visited.GetValueOrDefault(node.Id), null),
                ProgressionNodeKind.Badge => new PlayerProgressionNode(
                    node.Id, node.Kind, node.ResourceId,
                    badgeDetails.GetValueOrDefault(node.ResourceId)?.Name
                        ?? string.Empty,
                    badgeDetails.GetValueOrDefault(node.ResourceId)?.Description,
                    null, state.Active, state.Complete, false,
                    node.RequiresPrerequisites, null,
                    badgeDetails.GetValueOrDefault(node.ResourceId)?.ImageFileId),
                _ => throw new InvalidOperationException("Unsupported progression node type.")
            };
        }).ToArray();
        return new(true, true, graph.Revision, nodes,
            graph.Edges.Select(edge => new ProgressionEdgeDraft(
                edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.Condition)).ToArray(),
            badges);
    }

    private async Task<ProgressionGraphSnapshot?> ReadGraphAsync(
        Guid competitionId, CancellationToken ct)
    {
        var graph = await db.CompetitionProgressions.AsNoTracking()
            .Include(item => item.Nodes).Include(item => item.Edges)
            .AsSplitQuery().SingleOrDefaultAsync(item =>
                item.CompetitionId == competitionId, ct);
        return graph is null ? null : ProgressionGraphSnapshot.From(graph);
    }

    public async Task<IReadOnlyList<ProgressionBadgeDisplay>> ReadPublicUserBadgesAsync(
        Guid userId, CancellationToken ct) =>
        await db.UserBadgeGrants.AsNoTracking()
            .Where(grant => grant.UserId == userId && grant.Active)
            .Join(db.CompetitionBadges.AsNoTracking(), grant => grant.BadgeId,
                badge => badge.Id, (_, badge) => badge)
            .Where(badge => badge.DeletedAt == null)
            .Join(db.Competitions.AsNoTracking(), badge => badge.CompetitionId,
                competition => competition.Id,
                (badge, competition) => new { badge, competition })
            .Where(item => item.competition.DeletedAt == null
                && item.competition.AccessMode == CompetitionAccessMode.Public)
            .Select(item => new ProgressionBadgeDisplay(
                item.badge.Id, item.badge.CompetitionId,
                item.competition.Title, item.badge.Name,
                item.badge.Description, item.badge.ImageFileId))
            .Distinct().ToArrayAsync(ct);
}
