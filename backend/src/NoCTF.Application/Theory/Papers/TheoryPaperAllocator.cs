using System.Security.Cryptography;
using NoCTF.Domain.Theory.Activities;

namespace NoCTF.Application.Theory.Papers;

public sealed record TheoryAllocatedQuestion(int Attempt, Guid RuleId, Guid SourceId, Guid RevisionId, int Points);
public sealed record TheoryAllocationShortage(Guid RuleId, int Required, int Allocated);
public sealed record TheoryAllocationResult(IReadOnlyList<TheoryAllocatedQuestion> Questions,
    IReadOnlyList<TheoryAllocationShortage> Shortages)
{
    public bool Succeeded => Shortages.Count == 0;
}

/// <summary>
/// Finds a complete source-to-slot matching across every remaining attempt. The caller persists
/// only the current attempt; checking future slots prevents an unlucky draw stranding later papers.
/// </summary>
public static class TheoryPaperAllocator
{
    public static TheoryAllocationResult Allocate(IReadOnlyList<TheoryDrawRule> rules,
        IReadOnlySet<Guid> usedSources, int remainingAttempts, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(remainingAttempts, 1);
        if (rules.Count == 0 || rules.Any(rule => rule.Count <= 0 || rule.Points <= 0)
            || rules.Select(rule => rule.Id).Distinct().Count() != rules.Count)
            throw new ArgumentException("A validated non-empty rule set is required.", nameof(rules));

        var candidates = rules.Select(rule =>
        {
            var available = rule.Candidates.Where(candidate => !usedSources.Contains(candidate.SourceId)).ToArray();
            Shuffle(available);
            return available.GroupBy(candidate => candidate.SourceId).ToDictionary(group => group.Key, group => group.First());
        }).ToArray();
        var required = rules.Select(rule => checked(rule.Count * remainingAttempts)).ToArray();
        var individuallyShort = rules.Select((rule, index) => new TheoryAllocationShortage(rule.Id, required[index],
                Math.Min(required[index], candidates[index].Count)))
            .Where(shortage => shortage.Allocated < shortage.Required).ToArray();
        if (individuallyShort.Length > 0) return new([], individuallyShort);

        // Avoid expanding impossible requests into arbitrarily large slot arrays.
        var total = required.Aggregate(0L, (sum, count) => checked(sum + count));
        var sources = candidates.SelectMany(candidate => candidate.Keys).Distinct().Count();
        if (total > sources)
            return new([], rules.Select((rule, index) => new TheoryAllocationShortage(rule.Id, required[index], 0)).ToArray());

        var slots = Enumerable.Range(0, remainingAttempts)
            .SelectMany(attempt => rules.SelectMany((rule, index) => Enumerable.Range(0, rule.Count)
                .Select(_ => new Slot(attempt, index)))).ToArray();
        var traversal = Enumerable.Range(0, slots.Length).ToArray();
        Shuffle(traversal);
        var choices = candidates.Select(candidate => candidate.Keys.ToArray()).ToArray();
        var assigned = new Guid?[slots.Length];
        var owners = new Dictionary<Guid, int>();

        foreach (var root in traversal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queue = new Queue<int>();
            var visitedSlots = new HashSet<int> { root };
            var parent = new Dictionary<Guid, int>();
            queue.Enqueue(root);
            Guid? free = null;
            while (queue.TryDequeue(out var slot) && free is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var source in choices[slots[slot].RuleIndex])
                {
                    if (!parent.TryAdd(source, slot)) continue;
                    if (!owners.TryGetValue(source, out var owner)) { free = source; break; }
                    if (visitedSlots.Add(owner)) queue.Enqueue(owner);
                }
            }
            // Iterative augmenting path: no recursion/stack growth for large overlapping pools.
            while (free is { } source)
            {
                var slot = parent[source];
                free = assigned[slot];
                assigned[slot] = source;
                owners[source] = slot;
            }
        }

        var shortages = rules.Select((rule, index) => new TheoryAllocationShortage(rule.Id, required[index],
            slots.Select((slot, slotIndex) => (slot, slotIndex))
                .Count(pair => pair.slot.RuleIndex == index && assigned[pair.slotIndex] is not null)))
            .Where(shortage => shortage.Allocated < shortage.Required).ToArray();
        if (shortages.Length > 0) return new([], shortages);
        return new(slots.Select((slot, index) =>
        {
            var rule = rules[slot.RuleIndex];
            var source = assigned[index]!.Value;
            return new TheoryAllocatedQuestion(slot.Attempt, rule.Id, source,
                candidates[slot.RuleIndex][source].RevisionId, rule.Points);
        }).ToArray(), []);
    }

    public static void Shuffle<T>(T[] values)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var target = RandomNumberGenerator.GetInt32(index + 1);
            (values[index], values[target]) = (values[target], values[index]);
        }
    }

    private sealed record Slot(int Attempt, int RuleIndex);
}
