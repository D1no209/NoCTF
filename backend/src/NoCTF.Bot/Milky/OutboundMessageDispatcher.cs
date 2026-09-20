using NoCTF.Bot.Persistence;

namespace NoCTF.Bot.Milky;

public sealed class OutboundMessageDispatcher(
    BotStateStore store,
    MilkyClient milky,
    ILogger<OutboundMessageDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var item = store.ClaimDueOutbound();
            if (item is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                continue;
            }
            try
            {
                await milky.SendGroupMessageAsync(item.GroupId, item.Payload, stoppingToken);
                store.CompleteOutbound(item.Id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                store.FailOutbound(item.Id, item.AttemptCount, "shutdown_during_send");
                throw;
            }
            catch (Exception exception)
            {
                var safeError = exception is MilkyApiException milkyError
                    ? $"milky_retcode_{milkyError.Retcode}"
                    : exception.GetType().Name;
                store.FailOutbound(item.Id, item.AttemptCount, safeError);
                logger.LogWarning(
                    "Milky delivery {DeliveryId} to group {GroupId} failed with {ErrorType} on attempt {AttemptCount}.",
                    item.Id,
                    item.GroupId,
                    exception.GetType().Name,
                    item.AttemptCount);
            }
        }
    }
}
