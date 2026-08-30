using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Messaging;

internal static class MessagingInfrastructure
{
    internal static IServiceCollection AddNoCtfMessaging(
        this IServiceCollection services,
        bool exporting,
        bool development)
    {
        if (exporting)
            services.AddScoped<ITransactionalMessageOutbox, NoOpTransactionalMessageOutbox>();
        else if (development)
            services.AddScoped<ITransactionalMessageOutbox, DevelopmentTransactionalMessageOutbox>();
        else
            services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();

        services.AddScoped<IBackendMessagePublisher, WolverineBackendMessagePublisher>();
        return services;
    }
}
