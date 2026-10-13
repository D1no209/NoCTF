namespace NoCTF.Domain.Competitions.Awd;

public static class AwdGenerationPlan
{
    public static bool SameSchedule(AwdGenerationPolicy leftPolicy, int leftInterval,
        AwdGenerationPolicy rightPolicy, int rightInterval) => leftPolicy == rightPolicy
        && (leftPolicy == AwdGenerationPolicy.EveryRound || leftInterval == rightInterval);

    public static bool IsDue(AwdRoundRules rules, int roundNumber) =>
        rules.Options.GenerationPolicy != AwdGenerationPolicy.EveryNRounds
        || (roundNumber - rules.GenerationEpochRound) % rules.Options.GenerationInterval == 0;

    public static long Slot(AwdRoundRules rules, int roundNumber, long activeTicks) => rules.Options.GenerationPolicy switch
    {
        AwdGenerationPolicy.EveryRound => roundNumber - (long)rules.GenerationEpochRound,
        AwdGenerationPolicy.EveryNRounds => (roundNumber - (long)rules.GenerationEpochRound) / rules.Options.GenerationInterval,
        AwdGenerationPolicy.ActiveInterval => (activeTicks - rules.GenerationEpochActiveTicks)
            / TimeSpan.FromSeconds(rules.Options.GenerationInterval).Ticks,
        _ => throw new InvalidOperationException("Unknown AWD generation policy.")
    };
}
