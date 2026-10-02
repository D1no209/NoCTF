using System.Text.Json;
using NoCTF.CurrentImport;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;

namespace NoCTF.Tests.Unit.Administration;

public sealed class LifecycleAuditImportTests
{
    [Test]
    [Arguments(UserAccountLifecycleAction.EmailVerified)]
    [Arguments(UserAccountLifecycleAction.PhysicallyDeleted)]
    public async Task Imported_action_uses_the_typed_lifecycle_column(UserAccountLifecycleAction action)
    {
        using var content = JsonDocument.Parse(JsonSerializer.Serialize(new { schemaVersion = 1, action }));
        var notification = new UserAccountLifecycleChangedNotification();
        NoCTF.CurrentImport.Program.ApplyContent(notification, content.RootElement);
        await Assert.That(notification.UserLifecycleAction).IsEqualTo(action);
        await Assert.That(notification.ActionValue).IsNull();
    }

    [Test]
    [Arguments("{\"schemaVersion\":1}")]
    [Arguments("{\"schemaVersion\":1,\"action\":99}")]
    public async Task Unreadable_action_is_rejected(string json)
    {
        using var content = JsonDocument.Parse(json);
        await Assert.That(() => NoCTF.CurrentImport.Program.ApplyContent(new UserAccountLifecycleChangedNotification(), content.RootElement))
            .Throws<InvalidOperationException>();
    }
}
