using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Admission;

public sealed class RequestAdmissionCleanupAgent(
    IDbContextFactory<NoCtfDbContext> contexts,
    TimeProvider clock,
    ILogger<RequestAdmissionCleanupAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30), clock);
        do
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(stoppingToken);
                var now = clock.GetUtcNow();
                await db.RequestAdmissionLeases
                    .Where(item => item.ExpiresAt <= now)
                    .ExecuteDeleteAsync(stoppingToken);
                await db.RequestAdmissionWindows
                    .Where(item => item.ExpiresAt <= now)
                    .ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not remove expired request admission state.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
