namespace NoCTF.Application.Authentication.PasswordReset;

public enum PasswordResetRequestState
{
    Queued,
    Ignored,
    RateLimited,
    DeliveryNotConfigured
}

public enum PasswordResetCompletionState
{
    Reset,
    InvalidOrExpired
}

public interface IPasswordResetStore
{
    Task<PasswordResetRequestState> IssueAsync(
        string email,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<PasswordResetCompletionState> CompleteAsync(
        string token,
        string newPassword,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class RequestPasswordReset(IPasswordResetStore store)
{
    public async Task ExecuteAsync(
        string email,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        _ = await store.IssueAsync(email.Trim(), now, ct);
}

public sealed class CompletePasswordReset(IPasswordResetStore store)
{
    public Task<PasswordResetCompletionState> ExecuteAsync(
        string token,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.CompleteAsync(token, newPassword, now, ct);
}

public enum PasswordResetEmailDeliveryState
{
    Sent,
    NotConfigured,
    RecipientNotFound
}

public interface IPasswordResetEmailDelivery
{
    Task<PasswordResetEmailDeliveryState> SendResetAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken);

    Task<PasswordResetEmailDeliveryState> SendChangedNotificationAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
