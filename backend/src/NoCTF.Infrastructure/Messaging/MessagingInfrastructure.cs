using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Messaging;

internal static class MessagingInfrastructure
{
    internal static IServiceCollection AddNoCtfMessaging(
        this IServiceCollection services,
        bool exporting)
    {
        if (exporting)
            services.AddScoped<ITransactionalMessageOutbox, OpenApiTransactionalMessageOutbox>();
        else
            services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();

        services.AddScoped<IBackendMessagePublisher, WolverineBackendMessagePublisher>();
        return services;
    }
}
