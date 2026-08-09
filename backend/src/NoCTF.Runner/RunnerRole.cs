using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Observability;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Messages;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace NoCTF.Runner;

public static class RunnerRole
{
    public static IServiceCollection AddNoCtfStandaloneRunnerPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
            options => options.UseNpgsql(postgres).UseSnakeCaseNamingConvention());
        services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
        return services;
    }

    public static IServiceCollection AddNoCtfRunnerLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        PlatformLogService service = PlatformLogService.Runner) =>
        services.AddNoCtfPlatformLogging(configuration, service);

    public static void ConfigureNoCtfRunnerMessaging(
        this WolverineOptions options,
        IConfiguration configuration,
        bool durable = true)
    {
        var runnerPool = configuration["Runner:Pool"] ?? "default";
        var runnerId = configuration["Runner:Id"]
            ?? throw new InvalidOperationException("Runner:Id is required.");
        var poolQueueName = RunnerQueueName.FromPool(runnerPool);
        var nodeQueueName = RunnerNodeQueueName.FromAssignment(runnerPool, runnerId);

        options.Discovery.IncludeAssembly(typeof(RuntimeProviderHandler).Assembly);
        options.Durability.Mode = DurabilityMode.Balanced;
        options.Policies.OnException<TimeoutException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        options.Policies.OnException<System.Net.Http.HttpRequestException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        options.Policies.OnException<Npgsql.NpgsqlException>()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        options.Policies.OnException<DbUpdateConcurrencyException>().RetryTimes(5);
        if (durable)
        {
            options.ListenToPostgresqlQueue(poolQueueName.Value).UseDurableInbox();
            options.ListenToPostgresqlQueue(nodeQueueName.Value).UseDurableInbox();
        }
    }
}
