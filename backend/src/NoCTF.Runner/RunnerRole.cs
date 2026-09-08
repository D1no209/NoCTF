using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Messaging;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Observability;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Hosting.Messaging;
using NoCTF.Hosting;
using NoCTF.Runner.Messages;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.EntityFrameworkCore;
using Wolverine.Nats;

namespace NoCTF.Runner;

public static class RunnerRole
{
    public static IServiceCollection AddNoCtfStandaloneRunnerPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        // Runner writebacks update state and publish durable events in the same unit of work.
        // Plain AddDbContext leaves the transaction opened by the EF outbox uncommitted.
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
        var runnerId = configuration["Runner:Id"]
            ?? throw new InvalidOperationException("Runner:Id is required.");
        var nodeQueueName = RunnerNodeQueueName.FromRunnerId(runnerId);

        options.Discovery.IncludeAssembly(typeof(RuntimeProviderHandler).Assembly);
        options.Durability.Mode = DurabilityMode.Balanced;
        options.ConfigureNoCtfRunnerInfrastructureRetries();
        if (durable)
            options.ListenToNatsSubject(NatsSubjects.Runner(nodeQueueName.Value))
                .UseJetStream(NatsSubjects.RunnerStream, nodeQueueName.Value)
                .MaximumAckExtension(TimeSpan.FromSeconds(
                    AwdpFixExecutionBudget.JetStreamMaximumAckExtensionSeconds))
                .UseDurableInbox();
    }

}
