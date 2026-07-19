using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Submissions.Processing;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class FixUploadExpiryHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<FixUploadExpiryHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(
        5,
        configuration.GetValue("FixVerification:ExpiryScanIntervalSeconds", 30)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<ExpireFixUploads>()
                    .ExecuteAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (expired > 0)
                    logger.LogInformation("Expired {ExpiredFixUploadCount} unclaimed Fix uploads", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Fix upload expiry sweep failed");
            }
        }
    }
}
