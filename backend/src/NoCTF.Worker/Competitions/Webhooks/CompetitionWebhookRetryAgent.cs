using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Observability;

namespace NoCTF.Worker.Competitions.Webhooks;

/// <summary>Retries failed targets separately from the first-attempt JetStream queue.</summary>
public sealed class CompetitionWebhookRetryAgent(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<CompetitionWebhookRetryAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), clock);
        do
        {
            var started = Stopwatch.GetTimestamp();
            var scanRecorded = false;
            try
            {
                await using var claimScope = scopes.CreateAsyncScope();
                var store = claimScope.ServiceProvider
                    .GetRequiredService<ICompetitionWebhookDeliveryStore>();
                var due = await store.ClaimDueDeliveriesAsync(
                    clock.GetUtcNow(), 64, stoppingToken);
                NoCtfTelemetry.RecordWebhookRecoveryScan(
                    WebhookRecoveryScanKind.Retry,
                    due.Count == 0 ? WebhookRecoveryScanOutcome.Empty
                        : WebhookRecoveryScanOutcome.Work,
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                scanRecorded = true;
                await Parallel.ForEachAsync(due,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 16,
                        CancellationToken = stoppingToken
                    },
                    async (delivery, token) =>
                    {
                        try
                        {
                            await using var scope = scopes.CreateAsyncScope();
                            await scope.ServiceProvider
                                .GetRequiredService<CompetitionWebhookMessageHandler>()
                                .Handle(delivery, token);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            logger.LogWarning(exception,
                                "Webhook retry failed for event {EventId} and target {TargetId}.",
                                delivery.EventId, delivery.TargetId);
                        }
                    });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (!scanRecorded)
                    NoCtfTelemetry.RecordWebhookRecoveryScan(
                        WebhookRecoveryScanKind.Retry,
                        WebhookRecoveryScanOutcome.Failed,
                        Stopwatch.GetElapsedTime(started).TotalSeconds);
                logger.LogWarning(exception,
                    "Webhook retry scan failed; pending targets remain durable.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
