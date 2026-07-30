using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Administration;

public sealed record PlatformUserView(
    Guid Id,
    string UserName,
    string Email,
    UserKind Kind,
    UserRole Role,
    int TokenVersion,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum CreateBotState
{
    Created,
    UserNameConflict,
    InvalidUserName,
    InvalidRole
}

public sealed record CreateBotResult(
    CreateBotState State,
    PlatformUserView? User = null);

public enum IssueBotTokenFailure
{
    None,
    UserNotFound,
    UserIsNotBot,
    InvalidLifetime
}

public sealed record IssueBotTokenResult(
    IssuedAccessToken? Token,
    IssueBotTokenFailure Failure);

public static class BotIdentity
{
    public static string DummyEmail(Guid userId) => $"bot-{userId:N}@bot.invalid";
}

public sealed record DeadLetterView(
    Guid MessageId,
    string MessageType,
    string Source,
    string ExceptionType,
    DateTimeOffset SentAt,
    bool Replayable);

public interface IPlatformAdministrationStore
{
    Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken cancellationToken);
    Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<CreateBotResult> CreateBotAsync(
        string userName,
        UserRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<PlatformUserView?> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<DeadLetterView>> ListDeadLettersAsync(
        int limit,
        CancellationToken cancellationToken);
    Task<DeadLetterView?> FindDeadLetterAsync(
        Guid messageId,
        CancellationToken cancellationToken);
    Task<bool> RequeueDeadLetterAsync(
        Guid messageId,
        CancellationToken cancellationToken);
}

public sealed class ManagePlatform(
    IPlatformAdministrationStore store,
    IAccessTokenIssuer tokenIssuer)
{
    public const long MinimumBotTokenLifetimeSeconds = 60;
    public const long MaximumBotTokenLifetimeSeconds = 31_536_000;

    public Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken ct = default) =>
        store.ListUsersAsync(ct);
    public Task<PlatformUserView?> GetUserAsync(Guid userId, CancellationToken ct = default) =>
        store.FindUserAsync(userId, ct);
    public Task<CreateBotResult> CreateBotAsync(
        string userName,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalized = userName.Trim();
        if (normalized.Length is < 3 or > 64
            || !normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            return Task.FromResult(new CreateBotResult(CreateBotState.InvalidUserName));
        if (role != UserRole.Organizer)
            return Task.FromResult(new CreateBotResult(CreateBotState.InvalidRole));
        return store.CreateBotAsync(normalized, role, now, ct);
    }
    public async Task<IssueBotTokenResult> IssueBotTokenAsync(
        Guid userId,
        long expiresInSeconds,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (expiresInSeconds is < MinimumBotTokenLifetimeSeconds
            or > MaximumBotTokenLifetimeSeconds)
            return new(null, IssueBotTokenFailure.InvalidLifetime);
        var user = await store.FindUserAsync(userId, ct);
        if (user is null)
            return new(null, IssueBotTokenFailure.UserNotFound);
        if (user.Kind != UserKind.Bot)
            return new(null, IssueBotTokenFailure.UserIsNotBot);
        try
        {
            var lifetime = TimeSpan.FromTicks(checked(expiresInSeconds * TimeSpan.TicksPerSecond));
            var token = tokenIssuer.Issue(
                new AuthenticatedUser(
                    user.Id,
                    user.UserName,
                    user.Role,
                    user.Kind,
                    user.TokenVersion),
                now,
                lifetime);
            return new(token, IssueBotTokenFailure.None);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new(null, IssueBotTokenFailure.InvalidLifetime);
        }
        catch (OverflowException)
        {
            return new(null, IssueBotTokenFailure.InvalidLifetime);
        }
    }
    public Task<PlatformUserView?> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.UpdateRoleAsync(userId, role, now, ct);
    public Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.InvalidateTokensAsync(userId, now, ct);
    public Task<IReadOnlyList<DeadLetterView>> ListDeadLettersAsync(
        int limit,
        CancellationToken ct = default) =>
        store.ListDeadLettersAsync(limit, ct);
    public Task<DeadLetterView?> GetDeadLetterAsync(
        Guid messageId,
        CancellationToken ct = default) =>
        store.FindDeadLetterAsync(messageId, ct);
    public Task<bool> RequeueDeadLetterAsync(
        Guid messageId,
        CancellationToken ct = default) =>
        store.RequeueDeadLetterAsync(messageId, ct);
}
