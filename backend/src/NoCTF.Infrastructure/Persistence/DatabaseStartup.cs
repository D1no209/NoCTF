using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>Completes schema/bootstrap work before HTTP listeners and background consumers start.</summary>
public static class DatabaseStartup
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var timeout = configuration.GetValue("Database:StartupTimeoutSeconds", 180);
        var retryDelay = configuration.GetValue("Database:StartupRetryDelaySeconds", 2);
        if (timeout is < 1 or > 1800 || retryDelay is < 1 or > 60)
            throw new InvalidOperationException("Database startup timeout must be 1–1800 seconds and retry delay 1–60 seconds.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseStartup).FullName!);
        await RunAsync(
            ct => services.InitializeNoCtfAsync(ct),
            TimeSpan.FromSeconds(retryDelay),
            logger,
            deadline.Token);
    }

    internal static async Task RunAsync(
        Func<CancellationToken, Task> initialize,
        TimeSpan retryDelay,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await initialize(cancellationToken);
                logger.LogInformation("Database migrations and administrator bootstrap completed.");
                return;
            }
            catch (Exception exception) when (exception is DbException { IsTransient: true } or TimeoutException)
            {
                // No credentials or connection strings are written to the startup log.
                logger.LogWarning("Database is not ready ({FailureType}); retrying startup initialization.", exception.GetType().Name);
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }
}
