namespace NoCTF.Bot.Providers;

public enum ChatGroupRole
{
    Member,
    Admin,
    Owner
}

public sealed record ChatProviderCapabilities(
    bool SupportsGroupMessages,
    bool SupportsGroupMemberRoles,
    int MaximumTextLength);

public sealed record ChatProviderIdentity(string UserId, string DisplayName);

public sealed record ChatGroupMessage(
    string ProviderId,
    string GroupId,
    string SenderId,
    string MessageId,
    string Text);

public sealed record ChatDeliveryReceipt(string MessageId);

public interface IChatProvider
{
    string Id { get; }

    ChatProviderCapabilities Capabilities { get; }

    bool TryNormalizeUserId(string value, out string normalized);

    Task<ChatProviderIdentity> GetIdentityAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<ChatGroupMessage> ReadGroupMessagesAsync(
        CancellationToken cancellationToken);

    Task<ChatGroupRole> GetGroupMemberRoleAsync(
        string groupId,
        string userId,
        CancellationToken cancellationToken);

    Task<ChatDeliveryReceipt> SendGroupTextAsync(
        string groupId,
        string text,
        CancellationToken cancellationToken);
}
