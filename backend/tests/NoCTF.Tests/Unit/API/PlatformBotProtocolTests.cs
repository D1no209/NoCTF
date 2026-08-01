using System.Text.Json;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class PlatformBotProtocolTests
{
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("\"Organizer\"")]
    [Arguments("1")]
    public async Task CreateBot_accepts_named_and_legacy_numeric_role(string roleJson)
    {
        var request = JsonSerializer.Deserialize<CreatePlatformBotRequest>(
            $$"""{"userName":"repository-bot","role":{{roleJson}}}""",
            WebJson);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Role).IsEqualTo(UserRole.Organizer);
    }

    [Test]
    [Arguments("\"User\"")]
    [Arguments("0")]
    public async Task CreateBot_accepts_notification_relay_role(string roleJson)
    {
        var request = JsonSerializer.Deserialize<CreatePlatformBotRequest>(
            $$"""{"userName":"notification-relay","role":{{roleJson}}}""",
            WebJson);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Role).IsEqualTo(UserRole.User);
    }

    [Test]
    public async Task Role_conflict_code_serializes_as_a_named_enum()
    {
        var json = JsonSerializer.Serialize(
            UpdatePlatformUserRoleConflictCode.ActiveOwnerOrManagerAssignments);

        await Assert.That(json).IsEqualTo("\"ActiveOwnerOrManagerAssignments\"");
    }
}
