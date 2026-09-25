using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using NATS.Client.Core;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Messaging;

public static class MessagingInfrastructure
{
    internal static IServiceCollection AddNoCtfMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool exporting,
        bool development)
    {
        services.AddNoCtfNatsConnection(configuration);
        services.AddScoped<PostCommitDispatchStatus>();
        if (exporting)
            services.AddScoped<IPostCommitMessagePublisher, NoOpPostCommitMessagePublisher>();
        else
            services.AddScoped<IPostCommitMessagePublisher, WolverinePostCommitMessagePublisher>();

        services.AddScoped<IBackendMessagePublisher, WolverineBackendMessagePublisher>();
        return services;
    }

    public static IServiceCollection AddNoCtfNatsConnection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var url = configuration.GetConnectionString("Nats")
            ?? configuration["Wolverine:Nats:ConnectionString"];
        if (string.IsNullOrWhiteSpace(url))
            return services;
        services.TryAddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts
        {
            Url = url
        }));
        return services;
    }
}
