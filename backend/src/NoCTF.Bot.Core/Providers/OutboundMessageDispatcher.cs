using NoCTF.Bot.Persistence;

namespace NoCTF.Bot.Providers;

public sealed class OutboundMessageDispatcher(
    BotStateStore store,
    ChatProviderCatalog providers,
    ILogger<OutboundMessageDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var item = store.ClaimDueOutbound(providers.Active.Id);
            if (item is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                continue;
            }
            try
            {
                await providers.Active.SendGroupTextAsync(
                    item.GroupId,
                    item.Payload,
                    stoppingToken);
                store.CompleteOutbound(item.Id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Outbound message {MessageId} failed through {ProviderId} with {ErrorType}.",
                    item.Id,
                    providers.Active.Id,
                    exception.GetType().Name);
                store.FailOutbound(item.Id, item.AttemptCount, exception.GetType().Name);
            }
        }
    }
}
