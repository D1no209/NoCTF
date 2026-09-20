using NoCTF.Bot.Milky;
using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;

namespace NoCTF.Bot.Hosting;

public sealed class BotStartupValidationService(
    BotStateStore store,
    NoCtfClient noCtf,
    MilkyClient milky,
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
            var login = await milky.GetLoginInfoAsync(cancellationToken);
            var implementation = await milky.GetImplementationInfoAsync(cancellationToken);
            logger.LogInformation(
                "Milky is online as {Nickname} ({Uin}); implementation {Implementation} {Version}, protocol {MilkyVersion}.",
                login.Nickname,
                login.Uin,
                implementation.ImplName,
                implementation.ImplVersion,
                implementation.MilkyVersion);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Milky is offline during startup ({ErrorType}); event and delivery workers will retry.",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
