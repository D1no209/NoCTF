using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Challenges.Capabilities;

public static class BreakOutcomeUpdates
{
    public static void Apply(GameplayFact fact, BreakOutcome outcome)
    {
        if (outcome.GameplayFactId != fact.Id || fact.Kind != GameplayFactKind.BreakAttempt)
            throw new InvalidOperationException("Break evidence must belong to its attempt.");
        var current = fact.CapabilityOutcomes.OfType<BreakOutcome>().SingleOrDefault();
        if (current is null) { fact.CapabilityOutcomes.Add(outcome); return; }
        current.Match = outcome.Match;
        current.ObservedAt = outcome.ObservedAt;
        current.ExecutionState = outcome.ExecutionState;
        current.ExecutionFailure = outcome.ExecutionFailure;
        var ids = outcome.Flags.Select(flag => flag.FlagId).ToHashSet();
        current.Flags.RemoveAll(flag => !ids.Contains(flag.FlagId));
        foreach (var match in outcome.Flags)
        {
            var existing = current.Flags.SingleOrDefault(flag => flag.FlagId == match.FlagId);
            if (existing is null) current.Flags.Add(match);
            else existing.OwnerTeamId = match.OwnerTeamId;
        }
    }
}
