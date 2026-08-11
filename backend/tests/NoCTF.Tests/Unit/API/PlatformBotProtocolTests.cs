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
    public async Task Role_conflict_code_serializes_as_a_named_enum()
    {
        var json = JsonSerializer.Serialize(
            UpdatePlatformUserRoleConflictCode.ActiveOwnerOrManagerAssignments);

        await Assert.That(json).IsEqualTo("\"ActiveOwnerOrManagerAssignments\"");
    }

    [Test]
    public async Task Last_administrator_conflict_code_serializes_as_a_named_enum()
    {
        var json = JsonSerializer.Serialize(
            UpdatePlatformUserRoleConflictCode.LastAdministratorProtected);

        await Assert.That(json).IsEqualTo("\"LastAdministratorProtected\"");
    }
}
