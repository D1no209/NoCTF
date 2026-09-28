using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class PlatformLogUserIdProtectorTests
{
    [Test]
    public async Task Encrypted_user_id_round_trips_and_rejects_a_different_key()
    {
        var first = Create('a');
        var sameKey = Create('a');
        var differentKey = Create('b');
        var userId = Guid.NewGuid();
        var encrypted = first.Protect(userId);

        await Assert.That(encrypted).DoesNotContain(userId.ToString());
        await Assert.That(sameKey.Unprotect(encrypted)).IsEqualTo(userId);
        var rejected = false;
        try { _ = differentKey.Unprotect(encrypted); }
        catch (CryptographicException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
    }

    private static PlatformLogUserIdProtector Create(char secret) => new(
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RunnerScoring:SigningKey"] = new string(secret, 64)
            }).Build());
}
