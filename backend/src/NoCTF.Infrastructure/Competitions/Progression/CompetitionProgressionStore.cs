using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Competitions.Progression;

public sealed class CompetitionProgressionStore(
    NoCtfDbContext db,
    ProgressionReconciler reconciler,
    ICompetitionEventRecorder? eventRecorder = null,
    IPostCommitMessagePublisher? publisher = null,
    ProgressionGraphReadCache? graphs = null) : ICompetitionProgressionStore
{
    public async Task<CompetitionProgressionView?> ReadAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var mode = await db.Competitions.IgnoreAutoIncludes().AsNoTracking()
            .Where(item => item.Id == competitionId)
            .Select(item => (GameMode?)item.Mode)
            .SingleOrDefaultAsync(ct);
        if (mode != GameMode.Ctf)
            return null;
        var graph = await db.CompetitionProgressions.AsNoTracking()
            .Include(item => item.Nodes)
            .Include(item => item.Edges)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.CompetitionId == competitionId, ct);
        return graph is null
            ? new(competitionId, false, false, null, 0, [], [])
            : ToView(graph);
    }

    public async Task<CompetitionProgressionSaveResult> SaveAsync(
        SaveCompetitionProgressionCommand command,
        CancellationToken ct)
    {
        if (command.Nodes.Any(node => !Enum.IsDefined(node.Kind)))
            return new(null, CompetitionProgressionSaveFailure.InvalidGraph);
        var nodes = command.Nodes.Select(node => CreateNode(command.CompetitionId, node))
            .ToArray();
        var edges = command.Edges.Select(edge => new ProgressionEdge
        {
            Id = edge.Id,
            CompetitionId = command.CompetitionId,
            SourceNodeId = edge.SourceNodeId,
            TargetNodeId = edge.TargetNodeId,
            Condition = edge.Condition
        }).ToArray();
        var failure = ProgressionGraphRules.Validate(nodes, edges);
        if (failure is not null
            || nodes.Any(node => node.Id == Guid.Empty || node is ChallengeProgressionNode
                { CompetitionChallengeId: var challengeId } && challengeId == Guid.Empty
                || node is BadgeProgressionNode { CompetitionBadgeId: var badgeId }
                    && badgeId == Guid.Empty)
            || edges.Any(edge => edge.Id == Guid.Empty)
            || edges.Select(edge => edge.Id).Distinct().Count() != edges.Length)
            return new(null, CompetitionProgressionSaveFailure.InvalidGraph, failure);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var mode = await db.Competitions.IgnoreAutoIncludes().AsNoTracking()
            .Where(item => item.Id == command.CompetitionId)
            .Select(item => (GameMode?)item.Mode)
            .SingleOrDefaultAsync(ct);
        if (mode is null)
            return new(null, CompetitionProgressionSaveFailure.CompetitionNotFound);
        if (mode != GameMode.Ctf)
            return new(null, CompetitionProgressionSaveFailure.UnsupportedGameMode);

        var graph = await db.CompetitionProgressions
            .Include(item => item.Nodes)
            .Include(item => item.Edges)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.CompetitionId == command.CompetitionId, ct);
        if (graph?.ConcurrencyStamp != command.ExpectedConcurrencyStamp)
            return new(null, CompetitionProgressionSaveFailure.ConcurrencyConflict);
        if (graph is not null && nodes.Any(node => graph.Nodes.Any(existing =>
                existing.Id == node.Id && (existing.GetType() != node.GetType()
                    || existing is ChallengeProgressionNode existingChallenge
                        && node is ChallengeProgressionNode desiredChallenge
                        && existingChallenge.CompetitionChallengeId
                            != desiredChallenge.CompetitionChallengeId
                    || existing is BadgeProgressionNode existingBadge
                        && node is BadgeProgressionNode desiredBadge
                        && existingBadge.CompetitionBadgeId
                            != desiredBadge.CompetitionBadgeId))))
            return new(null, CompetitionProgressionSaveFailure.InvalidGraph);

        var challengeIds = nodes.OfType<ChallengeProgressionNode>()
            .Select(item => item.CompetitionChallengeId).ToArray();
        var actualChallenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.CompetitionId == command.CompetitionId
                && challengeIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToArrayAsync(ct);
        if (actualChallenges.Length != challengeIds.Length)
            return new(null, CompetitionProgressionSaveFailure.ChallengeNotFound);
        var badgeIds = nodes.OfType<BadgeProgressionNode>()
            .Select(item => item.CompetitionBadgeId).Distinct().ToArray();
        var actualBadges = await db.CompetitionBadges.AsNoTracking()
            .Where(item => item.CompetitionId == command.CompetitionId
                && item.DeletedAt == null
                && badgeIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToArrayAsync(ct);
        if (actualBadges.Length != badgeIds.Length)
            return new(null, CompetitionProgressionSaveFailure.BadgeNotFound);

        graph ??= new CompetitionProgression { CompetitionId = command.CompetitionId };
        if (db.Entry(graph).State == EntityState.Detached)
            db.CompetitionProgressions.Add(graph);
        ApplyNodes(graph, nodes);
        ApplyEdges(graph, edges);
        graph.Enabled = command.Enabled;
        graph.ShowPlayerMap = command.ShowPlayerMap;
        graph.Revision++;

        try
        {
            // Persist graph nodes before inserting team states that reference them.
            // Both saves share the same transaction; a failed reconciliation rolls back the graph.
            await db.SaveChangesAsync(ct);

            var teamIds = await db.Teams.IgnoreQueryFilters().AsNoTracking()
                .Where(team => team.CompetitionId == command.CompetitionId)
                .Select(team => team.Id)
                .ToArrayAsync(ct);
            await reconciler.ReconcileTeamsAsync(
                command.CompetitionId, teamIds, graph, command.Now, ct);

            if (eventRecorder is not null)
                await eventRecorder.RecordAsync(new(
                    command.CompetitionId,
                    CompetitionEventKind.CompetitionUpdated,
                    CompetitionEventLevel.Information,
                    CompetitionEventVisibility.Staff,
                    command.Now,
                    Reason: "Competition progression graph updated."), ct);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || TransactionFailureClassifier.IsRetryable(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            publisher?.DiscardPendingMessages();
            return new(null, CompetitionProgressionSaveFailure.ConcurrencyConflict);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            publisher?.DiscardPendingMessages();
            throw;
        }
        try
        {
            if (graphs is not null)
                await graphs.InvalidateAsync(command.CompetitionId, ct);
        }
        finally
        {
            if (publisher is not null)
                await publisher.FlushCommittedMessagesAsync();
        }
        return new(ToView(graph));
    }

    private static ProgressionNode CreateNode(Guid competitionId, ProgressionNodeDraft draft) =>
        draft.Kind switch
        {
            ProgressionNodeKind.Challenge => new ChallengeProgressionNode
            {
                Id = draft.Id,
                CompetitionId = competitionId,
                CompetitionChallengeId = draft.ResourceId
            },
            ProgressionNodeKind.Badge => new BadgeProgressionNode
            {
                Id = draft.Id,
                CompetitionId = competitionId,
                CompetitionBadgeId = draft.ResourceId
            },
            _ => throw new ArgumentOutOfRangeException(nameof(draft), draft.Kind, null)
        };

    private void ApplyNodes(
        CompetitionProgression graph,
        IReadOnlyCollection<ProgressionNode> desired)
    {
        var desiredById = desired.ToDictionary(item => item.Id);
        graph.Nodes.RemoveAll(item => !desiredById.ContainsKey(item.Id));
        var existingById = graph.Nodes.ToDictionary(item => item.Id);
        foreach (var node in desired)
        {
            if (!existingById.ContainsKey(node.Id))
            {
                graph.Nodes.Add(node);
                db.ProgressionNodes.Add(node);
            }
        }
    }

    private void ApplyEdges(
        CompetitionProgression graph,
        IReadOnlyCollection<ProgressionEdge> desired)
    {
        var desiredById = desired.ToDictionary(item => item.Id);
        graph.Edges.RemoveAll(item => !desiredById.ContainsKey(item.Id));
        var existingById = graph.Edges.ToDictionary(item => item.Id);
        foreach (var edge in desired)
        {
            if (existingById.TryGetValue(edge.Id, out var existing))
            {
                existing.SourceNodeId = edge.SourceNodeId;
                existing.TargetNodeId = edge.TargetNodeId;
                existing.Condition = edge.Condition;
            }
            else
            {
                graph.Edges.Add(edge);
                db.ProgressionEdges.Add(edge);
            }
        }
    }

    private static CompetitionProgressionView ToView(CompetitionProgression graph) => new(
        graph.CompetitionId,
        graph.Enabled,
        graph.ShowPlayerMap,
        graph.ConcurrencyStamp,
        graph.Revision,
        graph.Nodes.Select(node => node switch
        {
            ChallengeProgressionNode challenge => new ProgressionNodeDraft(
                challenge.Id, challenge.Kind, challenge.CompetitionChallengeId),
            BadgeProgressionNode badge => new ProgressionNodeDraft(
                badge.Id, badge.Kind, badge.CompetitionBadgeId),
            _ => throw new InvalidOperationException("Unsupported progression node type.")
        }).ToArray(),
        graph.Edges.Select(edge => new ProgressionEdgeDraft(
            edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.Condition)).ToArray());
}
