using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;
using NoCTF.Bot.Providers;

namespace NoCTF.Bot.Hosting;

public sealed class BotStartupValidationService(
    BotStateStore store,
    NoCtfClient noCtf,
    ChatProviderCatalog providers,
    ILogger<BotStartupValidationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        store.Initialize();
        var identity = await noCtf.GetCurrentUserAsync(cancellationToken);
        if (identity.State != NoCtfReadState.Available
            || identity.Value is null
            || !string.Equals(identity.Value.Role, "User", StringComparison.Ordinal)
            || !string.Equals(identity.Value.Kind, "Bot", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "NoCTF access token must belong to an active User-role Bot identity.");
        }
        logger.LogInformation(
            "Validated NoCTF Bot identity {UserName} ({UserId}).",
            identity.Value.UserName,
            identity.Value.UserId);
        try
        {
            var chatIdentity = await providers.Active.GetIdentityAsync(cancellationToken);
            logger.LogInformation(
                "Chat provider {ProviderId} is online as {DisplayName} ({UserId}).",
                providers.Active.Id,
                chatIdentity.DisplayName,
                chatIdentity.UserId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Chat provider {ProviderId} is offline during startup ({ErrorType}); event and delivery workers will retry.",
                providers.Active.Id,
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
