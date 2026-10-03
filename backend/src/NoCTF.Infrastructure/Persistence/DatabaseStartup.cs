using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>Completes schema/bootstrap work before HTTP listeners and background consumers start.</summary>
public static class DatabaseStartup
{
    public static IServiceCollection AddNoCtfDatabaseStartup(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AdministratorBootstrapper)))
            return services;
        services.AddOptions<SeedAdministratorOptions>()
            .Bind(configuration.GetSection(SeedAdministratorOptions.SectionName))
            .Validate(options => string.IsNullOrWhiteSpace(options.Password)
                || options.Password.Length is >= 8 and <= 1024,
                "SeedAdmin:Password must contain 8 to 1024 characters when configured.")
            .ValidateOnStart();
        services.TryAddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.TryAddScoped<AdministratorBootstrapper>();
        return services;
    }

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
        var migrationContentionRetries = 0;
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
            catch (DbException exception) when (migrationContentionRetries < 6
                && exception.SqlState is "42P07" or "42710" or "42701" or "23505")
            {
                // A peer may commit after this migrator read the applied list.
                // Re-enter MigrateAsync so it reads history under EF's lock again.
                // An incompatible pre-existing schema still fails after this bound.
                migrationContentionRetries++;
                logger.LogWarning("Startup migration contention ({FailureType}); rechecking migration history.", exception.GetType().Name);
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }
}
