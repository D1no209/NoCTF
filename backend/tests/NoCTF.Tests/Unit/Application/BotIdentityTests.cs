using NoCTF.Application.Administration;

namespace NoCTF.Tests.Unit.Application;

public sealed class BotIdentityTests
{
    [Test]
    public async Task DummyEmail_UsesStableReservedDomainAndUserId()
    {
        var id = Guid.Parse("0190f28d-58b5-7e47-bbb2-fd8f5a2b94cc");

        var email = BotIdentity.DummyEmail(id);

        await Assert.That(email)
            .IsEqualTo("bot-0190f28d58b57e47bbb2fd8f5a2b94cc@bot.invalid");
    }
}
