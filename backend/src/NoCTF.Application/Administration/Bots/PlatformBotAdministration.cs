using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Administration.Bots;

public enum CreatePlatformBotState
{
    Created,
    UserNameConflict
}

public sealed record CreatePlatformBotCommand(
    Guid BotId,
    string UserName,
    UserRole Role,
    DateTimeOffset Now);

public sealed record PlatformBotTokenSubject(
    Guid Id,
    string UserName,
    UserRole Role,
    int TokenVersion);

public interface IPlatformBotStore
{
    Task<(CreatePlatformBotState State, PlatformUserView? Bot)> CreateAsync(
        CreatePlatformBotCommand command,
        CancellationToken cancellationToken);

    Task<PlatformBotTokenSubject?> FindTokenSubjectAsync(
        Guid botId,
        CancellationToken cancellationToken);
}

public sealed class CreatePlatformBot(IPlatformBotStore store)
{
    public async Task<OperationResult<PlatformUserView>> ExecuteAsync(
        string userName,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalized = userName.Trim();
        if (normalized.Length is < 3 or > 64
            || !normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            return OperationResult<PlatformUserView>.Failure(
                "invalid_bot_username",
                "Bot UserName must contain 3..64 ASCII letters, digits, '_' or '-'.");
        }
        if (role != UserRole.Organizer)
        {
            return OperationResult<PlatformUserView>.Failure(
                "invalid_bot_role",
                "GitOps Bots must use the Organizer role.");
        }

        var result = await store.CreateAsync(
            new(Guid.CreateVersion7(now), normalized, role, now),
            ct);
        return result.State == CreatePlatformBotState.Created
            ? OperationResult<PlatformUserView>.Success(result.Bot!)
            : OperationResult<PlatformUserView>.Failure(
                "username_conflict",
                "The requested Bot UserName is already in use.");
    }
}

public sealed class IssuePlatformBotToken(
    IPlatformBotStore store,
    IAccessTokenIssuer issuer)
{
    public const int MinimumLifetimeSeconds = 60;
    public const int MaximumLifetimeSeconds = 31_536_000;

    public async Task<OperationResult<IssuedAccessToken>> ExecuteAsync(
        Guid botId,
        int expiresInSeconds,
        CancellationToken ct = default)
    {
        if (expiresInSeconds is < MinimumLifetimeSeconds or > MaximumLifetimeSeconds)
        {
            return OperationResult<IssuedAccessToken>.Failure(
                "invalid_token_lifetime",
                $"ExpiresInSeconds must be between {MinimumLifetimeSeconds} and {MaximumLifetimeSeconds}.");
        }

        var bot = await store.FindTokenSubjectAsync(botId, ct);
        if (bot is null)
        {
            return OperationResult<IssuedAccessToken>.Failure(
                "bot_not_found",
                "Bot was not found.");
        }

        return OperationResult<IssuedAccessToken>.Success(
            issuer.Issue(
                new(bot.Id, bot.UserName, bot.Role.ToString(), bot.TokenVersion),
                TimeSpan.FromSeconds(expiresInSeconds)));
    }
}
