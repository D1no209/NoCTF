using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Observability;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.Runtime.Agents;

namespace NoCTF.Worker;

public static class WorkerRole
{
    public static IServiceCollection AddNoCtfWorkerRole(this IServiceCollection services)
    {
        services.AddSingularAgent<MaintenanceTickAgent>();
        return services;
    }

    public static IServiceCollection AddNoCtfWorkerLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        PlatformLogService service = PlatformLogService.Worker) =>
        services.AddNoCtfPlatformLogging(configuration, service);

    public static void ConfigureNoCtfWorkerMessaging(
        this WolverineOptions options,
        bool durable = true)
    {
        options.Discovery.IncludeType(typeof(BackendMessageHandlers));
        options.Discovery.IncludeType(typeof(CompetitionNotificationMessageHandlers));
        options.Discovery.IncludeType(typeof(CompetitionEventMessageHandlers));
        options.Discovery.IncludeType(typeof(DataExportMessageHandlers));
        options.Durability.Mode = DurabilityMode.Balanced;
        options.Durability.CheckAssignmentPeriod = TimeSpan.FromSeconds(1);
        options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
        options.Durability.ScheduledJobFirstExecution = TimeSpan.FromSeconds(1);
        options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
        options.Policies.OnException<TimeoutException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        options.Policies.OnException<System.Net.Http.HttpRequestException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        options.Policies.OnException<EmailVerificationDeliveryException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        options.Policies.OnException<Npgsql.NpgsqlException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        options.Policies.OnException<DbUpdateConcurrencyException>().RetryTimes(5);
        if (durable)
            options.ListenToPostgresqlQueue("noctf-worker").UseDurableInbox();
    }
}
