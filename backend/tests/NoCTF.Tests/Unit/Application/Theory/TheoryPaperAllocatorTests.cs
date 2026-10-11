using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Theory.Activities;

namespace NoCTF.Tests.Unit.Application.Theory;

public sealed class TheoryPaperAllocatorTests
{
    [Test]
    public async Task Overlapping_rules_are_rematched_instead_of_greedily_exhausted()
    {
        var common = Guid.NewGuid();
        var alternative = Guid.NewGuid();
        var broad = Rule(common, alternative);
        var narrow = Rule(common);
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var result = TheoryPaperAllocator.Allocate([broad, narrow], new HashSet<Guid>(), 1);
            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(result.Questions.Single(question => question.RuleId == narrow.Id).SourceId).IsEqualTo(common);
            await Assert.That(result.Questions.Single(question => question.RuleId == broad.Id).SourceId).IsEqualTo(alternative);
        }
    }

    [Test]
    public async Task Duplicate_versions_and_copies_share_one_source_identity()
    {
        var source = Guid.NewGuid();
        var rule = Rule(source, source, source);
        rule.Count = 2;
        var result = TheoryPaperAllocator.Allocate([rule], new HashSet<Guid>(), 1);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Questions.Count).IsEqualTo(0);
        await Assert.That(result.Shortages.Single().Allocated).IsEqualTo(1);
    }

    [Test]
    public async Task Previously_issued_sources_are_excluded_even_when_their_revision_changes()
    {
        var old = Guid.NewGuid();
        var unseen = Guid.NewGuid();
        var result = TheoryPaperAllocator.Allocate([Rule(old, unseen)], new HashSet<Guid> { old }, 1);
        await Assert.That(result.Questions.Single().SourceId).IsEqualTo(unseen);
    }

    [Test]
    public async Task Capacity_covers_all_attempts_not_just_the_current_one()
    {
        var sources = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var broad = Rule(sources);
        var narrow = Rule(sources.Take(2).ToArray());
        var result = TheoryPaperAllocator.Allocate([broad, narrow], new HashSet<Guid>(), 2);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Questions.Select(question => question.SourceId).Distinct().Count()).IsEqualTo(4);
        var first = result.Questions.Where(question => question.Attempt == 0).Select(question => question.SourceId).ToHashSet();
        await Assert.That(TheoryPaperAllocator.Allocate([broad, narrow], first, 1).Succeeded).IsTrue();
        await Assert.That(TheoryPaperAllocator.Allocate([broad, narrow], new HashSet<Guid>(), 3).Succeeded).IsFalse();
    }

    [Test]
    public async Task Hall_deficiency_is_detected_even_when_each_rule_and_total_capacity_look_sufficient()
    {
        var shared = Guid.NewGuid();
        var result = TheoryPaperAllocator.Allocate([Rule(shared), Rule(shared), Rule(Guid.NewGuid(), Guid.NewGuid())],
            new HashSet<Guid>(), 1);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Questions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Matching_agrees_with_exhaustive_search_on_all_small_candidate_graphs()
    {
        var sources = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        for (var graph = 0; graph < 512; graph++)
        {
            var rules = Enumerable.Range(0, 3).Select(index => Rule(sources
                .Where((_, source) => (graph & (1 << (index * 3 + source))) != 0).ToArray())).ToArray();
            var feasible = sources.Any(a => sources.Any(b => b != a && sources.Any(c => c != a && c != b
                && rules[0].Candidates.Any(candidate => candidate.SourceId == a)
                && rules[1].Candidates.Any(candidate => candidate.SourceId == b)
                && rules[2].Candidates.Any(candidate => candidate.SourceId == c))));
            await Assert.That(TheoryPaperAllocator.Allocate(rules, new HashSet<Guid>(), 1).Succeeded).IsEqualTo(feasible);
        }
    }

    private static TheoryDrawRule Rule(params Guid[] sources) => new()
    {
        Id = Guid.NewGuid(), Count = 1, Points = 10,
        Candidates = sources.Select(source => new TheoryDrawCandidate { SourceId = source, RevisionId = Guid.NewGuid() }).ToList()
    };
}
