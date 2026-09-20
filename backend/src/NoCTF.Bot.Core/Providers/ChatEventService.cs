using NoCTF.Bot.Commands;

namespace NoCTF.Bot.Providers;

public sealed class ChatEventService(
    ChatProviderCatalog providers,
    BotCommandProcessor commands,
    ILogger<ChatEventService> logger) : BackgroundService
{
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1)
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failureCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var message in providers.Active
                    .ReadGroupMessagesAsync(stoppingToken))
                {
                    failureCount = 0;
                    await commands.ProcessAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var delay = ReconnectDelays[Math.Min(failureCount, ReconnectDelays.Length - 1)];
                failureCount++;
                logger.LogWarning(
                    "Chat provider {ProviderId} event stream is unavailable ({ErrorType}); retrying in {DelaySeconds}s.",
                    providers.Active.Id,
                    exception.GetType().Name,
                    delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
