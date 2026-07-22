using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Composition;

public sealed class RunnerResourceReaperHostedService(
    IEnumerable<IRuntimeResourceReaper> reapers,
    IConfiguration configuration,
    ILogger<RunnerResourceReaperHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(
        5, configuration.GetValue("Runtime:ReaperIntervalSeconds", 60)));
    private readonly TimeSpan temporaryDirectoryLifetime = TimeSpan.FromSeconds(Math.Max(
        60, configuration.GetValue("Runtime:TemporaryDirectoryLifetimeSeconds", 3600)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SweepAsync(stoppingToken);
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var reaper in reapers)
        {
            try
            {
                var result = await reaper.ReapExpiredAsync(now, cancellationToken);
                if (result.FailedCount > 0)
                    logger.LogWarning("Runner resource reaper removed {RemovedCount} resources and failed to remove {FailedCount}",
                        result.RemovedCount, result.FailedCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Runner resource reaper sweep failed");
            }
        }
        ReapTemporaryDirectories(now);
    }

    private void ReapTemporaryDirectories(DateTimeOffset now)
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        foreach (var directory in Directory.EnumerateDirectories(root, "noctf-awdp-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fullPath = Path.GetFullPath(directory);
                if (!string.Equals(Path.GetDirectoryName(fullPath)?.TrimEnd(Path.DirectorySeparatorChar),
                        root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Directory.GetLastWriteTimeUtc(fullPath) > now.UtcDateTime - temporaryDirectoryLifetime)
                    continue;
                Directory.Delete(fullPath, recursive: true);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Runner could not remove an expired AWDP temporary directory");
            }
        }
    }
}
