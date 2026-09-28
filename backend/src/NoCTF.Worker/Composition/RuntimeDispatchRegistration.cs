using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Worker.Composition;

internal static class RuntimeDispatchRegistration
{
    public static IServiceCollection AddRuntimeDispatchWakeupGate(
        this IServiceCollection services, bool distributed)
    {
        services.AddSingleton(provider => new RuntimeDispatchWakeupGate(
            distributed ? provider.GetRequiredService<INatsConnection>() : null));
        return services;
    }
}
