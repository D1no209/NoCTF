using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.Application;

public sealed class EmailCanonicalizerTests
{
    [Test]
    public async Task Canonicalization_trims_and_lowercases_invariantly()
    {
        await Assert.That(EmailCanonicalizer.Canonicalize("  PLAYER@Example.TEST  "))
            .IsEqualTo("player@example.test");
        await Assert.That(EmailCanonicalizer.Canonicalize("I@example.test"))
            .IsEqualTo("i@example.test");
    }
}
