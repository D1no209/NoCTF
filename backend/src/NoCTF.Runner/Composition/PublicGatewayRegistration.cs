using NoCTF.Infrastructure.Runtime.PublicAccess;
using NoCTF.Runtime.Docker.PublicAccess;
using NoCTF.Application.Runtime.PublicAccess;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NoCTF.Runner.PublicAccess;

internal static class PublicGatewayRegistration
{
    public static IServiceCollection AddPublicGatewayCoordinator(this IServiceCollection services, IConfiguration configuration)
    {
        var connector = configuration["PublicGateway:ConnectorId"];
        if (string.IsNullOrWhiteSpace(connector)) return services;
        services.AddNoCtfPublicGateway(configuration, standaloneRunner: true);
        var capability = services.LastOrDefault(item => item.ServiceType == typeof(PublicGatewayCapability))?.ImplementationInstance as PublicGatewayCapability;
        if (capability?.NamespaceIsolationAvailable != true) return services;
        var runnerId = configuration["PublicGateway:RunnerId"];
        if (runnerId != configuration["Runner:Id"]) return services;
        var image = configuration["PublicGateway:HelperImage"] ?? "";
        var keys = new[] { "ServerHost", "ServerName", "CaFile", "CertificateFile", "KeyFile", "TokenFile" };
        if (!System.Text.RegularExpressions.Regex.IsMatch(image, @"^[a-zA-Z0-9./:_-]+@sha256:[a-f0-9]{64}$")
            || keys.Any(key => string.IsNullOrWhiteSpace(configuration["PublicGateway:" + key]))
            || !PublicGatewayPolicyRules.Host(configuration["PublicGateway:ServerHost"] ?? ""))
        {
            services.Replace(ServiceDescriptor.Singleton(capability with
            { NamespaceIsolationAvailable = false, ConfigurationError = "Gateway transport requires a digest-pinned image, valid server host, and pairing credential files." }));
            return services;
        }
        string Required(string key) => configuration["PublicGateway:" + key]
            ?? throw new InvalidOperationException($"PublicGateway:{key} is required for the paired Runner.");
        services.AddSingleton(new GatewayTransportOptions(connector, runnerId!, image,
            Required("ServerHost"), configuration.GetValue("PublicGateway:ServerPort", 7001), Required("ServerName"),
            Required("CaFile"), Required("CertificateFile"), Required("KeyFile"), Required("TokenFile")));
        services.AddSingleton<DockerPublicGateway>();
        services.AddSingleton<PublicGatewayAgent>();
        services.AddHostedService(provider => provider.GetRequiredService<PublicGatewayAgent>());
        return services;
    }
}
