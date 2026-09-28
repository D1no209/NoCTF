using NoCTF.Domain.Competitions.Progression;

namespace NoCTF.Tests.Unit.Competitions;

public sealed class ProgressionGraphRulesTests
{
    [Test]
    public async Task Root_nodes_are_active_and_badge_instances_use_or()
    {
        var challenge = Challenge();
        var badgeId = Guid.NewGuid();
        var firstBadge = Badge(badgeId);
        var secondBadge = Badge(badgeId);
        var graph = ProgressionGraphRules.Evaluate(
            [challenge, firstBadge, secondBadge], [], new HashSet<Guid>());

        await Assert.That(graph.Nodes.Values.All(node => node.Active)).IsTrue();
        await Assert.That(graph.Nodes[challenge.Id].Complete).IsFalse();
        await Assert.That(graph.ActiveBadgeIds.Contains(badgeId)).IsTrue();
        await Assert.That(graph.ActiveBadgeIds.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Completed_and_incomplete_predecessors_are_combined_with_and()
    {
        var completed = Challenge();
        var incomplete = Challenge();
        var target = Challenge();
        var edges = new[]
        {
            Edge(completed, target, ProgressionPrerequisiteCondition.Completed),
            Edge(incomplete, target, ProgressionPrerequisiteCondition.Incomplete)
        };

        var unlocked = ProgressionGraphRules.Evaluate(
            [completed, incomplete, target], edges,
            new HashSet<Guid> { completed.CompetitionChallengeId });
        var relocked = ProgressionGraphRules.Evaluate(
            [completed, incomplete, target], edges,
            new HashSet<Guid> { completed.CompetitionChallengeId,
                incomplete.CompetitionChallengeId, target.CompetitionChallengeId });

        await Assert.That(unlocked.Nodes[target.Id].Active).IsTrue();
        await Assert.That(relocked.Nodes[target.Id].Active).IsFalse();
        await Assert.That(relocked.Nodes[target.Id].Complete).IsTrue();
    }

    [Test]
    public async Task Negative_predecessor_is_true_before_any_attempt_and_relocks_after_success()
    {
        var source = Challenge();
        var target = Challenge();
        var edge = Edge(source, target, ProgressionPrerequisiteCondition.Incomplete);

        var before = ProgressionGraphRules.Evaluate([source, target], [edge], new HashSet<Guid>());
        var after = ProgressionGraphRules.Evaluate([source, target], [edge],
            new HashSet<Guid> { source.CompetitionChallengeId });

        await Assert.That(before.Nodes[target.Id].Active).IsTrue();
        await Assert.That(after.Nodes[target.Id].Active).IsFalse();
    }

    [Test]
    public async Task Challenge_can_ignore_unmet_predecessors_without_completing_itself()
    {
        var source = Challenge();
        var target = Challenge();
        target.RequiresPrerequisites = false;
        var edge = Edge(source, target, ProgressionPrerequisiteCondition.Completed);

        var evaluation = ProgressionGraphRules.Evaluate(
            [source, target], [edge], new HashSet<Guid>());

        await Assert.That(evaluation.Nodes[target.Id].Active).IsTrue();
        await Assert.That(evaluation.Nodes[target.Id].Complete).IsFalse();
    }

    [Test]
    public async Task Unconditional_badge_activates_its_specific_node_and_successors()
    {
        var source = Challenge();
        var badgeId = Guid.NewGuid();
        var badge = Badge(badgeId);
        badge.RequiresPrerequisites = false;
        var successor = Challenge();
        var edges = new[]
        {
            Edge(source, badge, ProgressionPrerequisiteCondition.Completed),
            Edge(badge, successor, ProgressionPrerequisiteCondition.Completed)
        };

        var evaluation = ProgressionGraphRules.Evaluate(
            [source, badge, successor], edges, new HashSet<Guid>());

        await Assert.That(evaluation.Nodes[badge.Id].Active).IsTrue();
        await Assert.That(evaluation.Nodes[badge.Id].Complete).IsTrue();
        await Assert.That(evaluation.Nodes[successor.Id].Active).IsTrue();
        await Assert.That(evaluation.ActiveBadgeIds.Contains(badgeId)).IsTrue();
    }

    [Test]
    public async Task Badge_predecessor_refers_to_the_specific_instance()
    {
        var solved = Challenge();
        var badgeId = Guid.NewGuid();
        var firstPath = Badge(badgeId);
        var secondPath = Badge(badgeId);
        var target = Challenge();
        var edges = new[]
        {
            Edge(solved, firstPath, ProgressionPrerequisiteCondition.Completed),
            Edge(firstPath, target, ProgressionPrerequisiteCondition.Completed)
        };

        var evaluation = ProgressionGraphRules.Evaluate(
            [solved, firstPath, secondPath, target], edges, new HashSet<Guid>());

        await Assert.That(evaluation.Nodes[firstPath.Id].Active).IsFalse();
        await Assert.That(evaluation.Nodes[secondPath.Id].Active).IsTrue();
        await Assert.That(evaluation.ActiveBadgeIds.Contains(badgeId)).IsTrue();
        await Assert.That(evaluation.Nodes[target.Id].Active).IsFalse();
    }

    [Test]
    public async Task Cycles_self_references_and_duplicate_challenges_are_rejected()
    {
        var first = Challenge();
        var second = Challenge();
        var cycle = new[]
        {
            Edge(first, second, ProgressionPrerequisiteCondition.Completed),
            Edge(second, first, ProgressionPrerequisiteCondition.Incomplete)
        };

        await Assert.That(ProgressionGraphRules.Validate([first, second], cycle))
            .IsEqualTo(ProgressionGraphFailure.Cycle);
        await Assert.That(ProgressionGraphRules.Validate(
                [first], [Edge(first, first, ProgressionPrerequisiteCondition.Completed)]))
            .IsEqualTo(ProgressionGraphFailure.SelfReference);
        await Assert.That(ProgressionGraphRules.Validate(
                [first], [Edge(first, second, ProgressionPrerequisiteCondition.Completed)]))
            .IsEqualTo(ProgressionGraphFailure.MissingEndpoint);
        await Assert.That(ProgressionGraphRules.Validate(
                [first, new ChallengeProgressionNode
                {
                    Id = Guid.NewGuid(),
                    CompetitionChallengeId = first.CompetitionChallengeId
                }], []))
            .IsEqualTo(ProgressionGraphFailure.DuplicateChallenge);
    }

    private static ChallengeProgressionNode Challenge() => new()
    {
        Id = Guid.NewGuid(),
        CompetitionChallengeId = Guid.NewGuid()
    };

    private static BadgeProgressionNode Badge(Guid badgeId) => new()
    {
        Id = Guid.NewGuid(),
        CompetitionBadgeId = badgeId
    };

    private static ProgressionEdge Edge(
        ProgressionNode source,
        ProgressionNode target,
        ProgressionPrerequisiteCondition condition) => new()
    {
        Id = Guid.NewGuid(),
        SourceNodeId = source.Id,
        TargetNodeId = target.Id,
        Condition = condition
    };
}
