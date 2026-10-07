using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Competitions.StaffWebhooks;
using Wolverine;

namespace NoCTF.Worker.Competitions.StaffWebhooks;

public sealed class StaffWebhookAgent(IServiceScopeFactory scopes, TimeProvider clock, ILogger<StaffWebhookAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovering = true;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IStaffWebhookStore>();
                await store.TickAsync(recovering, stoppingToken); recovering = false;
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                foreach (var delivery in await store.ClaimAsync(100, stoppingToken)) await bus.PublishAsync(delivery);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Staff webhook recovery scan failed; durable pending deliveries will be retried."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
