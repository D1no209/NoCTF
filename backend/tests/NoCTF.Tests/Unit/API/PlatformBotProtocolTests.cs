using System.Text.Json;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class PlatformBotProtocolTests
{
    [Test]
    [Arguments("\"Organizer\"")]
    [Arguments("1")]
    public async Task CreateBot_accepts_named_and_legacy_numeric_role(string roleJson)
    {
        var request = JsonSerializer.Deserialize<CreatePlatformBotRequest>(
            $$"""{"userName":"repository-bot","role":{{roleJson}}}""");

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Role).IsEqualTo(UserRole.Organizer);
    }
}
