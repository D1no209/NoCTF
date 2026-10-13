using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.GameModes.Awd.Flags;

public static class AwdFlagLifetime
{
    public static void Assign(AwdFlagIssue issue, AwdRuleOptions options, int round, long activeTicks)
    {
        if (options.FlagLifetime <= 0 || options.HistoricalLifetime <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        issue.ActiveStartTicks = activeTicks;
        issue.ExpiryRound = options.FlagLifetimeUnit == AwdLifetimeUnit.Rounds
            ? checked(round + options.FlagLifetime) : null;
        issue.ActiveExpiryTicks = options.FlagLifetimeUnit == AwdLifetimeUnit.ActiveSeconds
            ? checked(activeTicks + TimeSpan.FromSeconds(options.FlagLifetime).Ticks) : null;
        var historyUnit = options.LinkHistoricalWindow ? options.FlagLifetimeUnit : options.HistoricalLifetimeUnit;
        var historyLife = options.LinkHistoricalWindow ? options.FlagLifetime : options.HistoricalLifetime;
        issue.HistoricalExpiryRound = historyUnit == AwdLifetimeUnit.Rounds ? checked(round + historyLife) : null;
        issue.HistoricalActiveExpiryTicks = historyUnit == AwdLifetimeUnit.ActiveSeconds
            ? checked(activeTicks + TimeSpan.FromSeconds(historyLife).Ticks) : null;
    }

    public static bool Accepts(AwdFlagIssue issue, int round, long activeTicks) =>
        issue.State == AwdFlagIssueState.Issued && round >= issue.RoundNumber
        && activeTicks >= issue.ActiveStartTicks
        && (issue.ExpiryRound is not { } expiryRound || round < expiryRound)
        && (issue.ActiveExpiryTicks is not { } expiryTicks || activeTicks < expiryTicks);

    public static bool RequiresRead(AwdFlagIssue issue, int round, long activeTicks) =>
        issue.State == AwdFlagIssueState.Issued && round >= issue.RoundNumber
        && activeTicks >= issue.ActiveStartTicks
        && (issue.HistoricalExpiryRound is not { } expiryRound || round < expiryRound)
        && (issue.HistoricalActiveExpiryTicks is not { } expiryTicks || activeTicks < expiryTicks);
}
