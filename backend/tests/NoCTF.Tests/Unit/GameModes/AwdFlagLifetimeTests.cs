using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Flags;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdFlagLifetimeTests
{
    [Test]
    public async Task FiveRoundsIncludeIssuanceAndExcludeRoundFifteen()
    {
        var issue = new AwdFlagIssue { RoundNumber = 10, State = AwdFlagIssueState.Issued };
        AwdFlagLifetime.Assign(issue, new(), 10, 0);
        await Assert.That(AwdFlagLifetime.Accepts(issue, 14, TimeSpan.FromDays(1).Ticks)).IsTrue();
        await Assert.That(AwdFlagLifetime.Accepts(issue, 15, TimeSpan.FromDays(1).Ticks)).IsFalse();
        await Assert.That(AwdFlagLifetime.Accepts(issue, 9, 0)).IsFalse();
    }

    [Test]
    public async Task ActiveTimeAndHistoricalObligationAreIndependent()
    {
        var issue = new AwdFlagIssue { RoundNumber = 1, State = AwdFlagIssueState.Issued };
        AwdFlagLifetime.Assign(issue, new()
        {
            FlagLifetimeUnit = AwdLifetimeUnit.ActiveSeconds, FlagLifetime = 60,
            LinkHistoricalWindow = false, HistoricalLifetime = 2
        }, 1, 100);
        await Assert.That(AwdFlagLifetime.Accepts(issue, 1, 100 + TimeSpan.FromSeconds(60).Ticks)).IsFalse();
        await Assert.That(AwdFlagLifetime.RequiresRead(issue, 2, 100 + TimeSpan.FromSeconds(60).Ticks)).IsTrue();
        await Assert.That(AwdFlagLifetime.RequiresRead(issue, 3, 100 + TimeSpan.FromSeconds(60).Ticks)).IsFalse();
    }

    [Test]
    [Arguments(AwdFlagIssueState.Planned)]
    [Arguments(AwdFlagIssueState.Revoked)]
    public async Task OnlyIssuedFlagsAreAcceptable(AwdFlagIssueState state)
    {
        var issue = new AwdFlagIssue { RoundNumber = 1, State = state };
        AwdFlagLifetime.Assign(issue, new(), 1, 0);
        await Assert.That(AwdFlagLifetime.Accepts(issue, 1, 0)).IsFalse();
    }
}
