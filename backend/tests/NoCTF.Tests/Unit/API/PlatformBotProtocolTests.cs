using System.Text.Json;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class PlatformBotProtocolTests
{
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("\"Organizer\"")]
    public async Task CreateBot_accepts_named_role(string roleJson)
    {
        var request = JsonSerializer.Deserialize<CreatePlatformBotRequest>(
            $$"""{"userName":"repository-bot","role":{{roleJson}}}""",
            WebJson);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Role).IsEqualTo(UserRoleProtocol.Organizer);
    }

    [Test]
    [Arguments("\"User\"")]
    public async Task CreateBot_accepts_notification_relay_role(string roleJson)
    {
        var request = JsonSerializer.Deserialize<CreatePlatformBotRequest>(
            $$"""{"userName":"notification-relay","role":{{roleJson}}}""",
            WebJson);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Role).IsEqualTo(UserRoleProtocol.User);
    }

    [Test]
    public async Task Platform_user_patch_serializes_bounded_fields_as_named_enums()
    {
        var json = JsonSerializer.Serialize(new PatchPlatformUserRequest
        {
            Role = UserRoleProtocol.Organizer,
            AccountStatus = PlatformManagedUserAccountStatusProtocol.Disabled
        }, WebJson);

        await Assert.That(json).Contains("\"role\":\"Organizer\"");
        await Assert.That(json).Contains("\"accountStatus\":\"Disabled\"");
    }
}
