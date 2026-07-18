using Microsoft.Extensions.DependencyInjection;

namespace NoCTF.Integrations.QQBot;

public static class ServiceRegistration
{
    public static IServiceCollection AddQqBotIntegration(this IServiceCollection services, Uri baseAddress)
    {
        services.AddHttpClient<IQqBotTransport, QqBotTransport>(client => client.BaseAddress = baseAddress);
        return services;
    }
}
