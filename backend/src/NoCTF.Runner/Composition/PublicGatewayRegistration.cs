using NoCTF.Infrastructure.Runtime.PublicAccess;
using NoCTF.Runtime.Docker.PublicAccess;
using NoCTF.Application.Runtime.PublicAccess;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NoCTF.Domain.Platform;

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
        if (capability.Transport == PublicGatewayTransportKind.SharedSsh)
        {
            string Read(string key) => configuration["PublicGateway:" + key] ?? "";
            static bool Image(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"^[a-zA-Z0-9./:_-]+@sha256:[a-f0-9]{64}$");
            static bool DirectoryPath(string value) => value.StartsWith('/') && value.Length is > 1 and < 512 && !value.Any(char.IsControl)
                && value.Split('/').All(part => part is not ("." or ".."));
            var serverPort = configuration.GetValue("PublicGateway:ServerPort", 60999);
            if (!OperatingSystem.IsLinux() || !Image(Read("ClientImage")) || !Image(Read("RelayImage"))
                || !DirectoryPath(Read("LocalStateDirectory")) || !DirectoryPath(Read("HostStateDirectory"))
                || !DirectoryPath(Read("PrivateKeyFile")) || !DirectoryPath(Read("KnownHostsFile"))
                || !PublicGatewayPolicyRules.Host(Read("ServerHost")) || serverPort is < 32768 or > 60999)
            {
                services.Replace(ServiceDescriptor.Singleton(capability with { NamespaceIsolationAvailable = false,
                    ConfigurationError = "Shared SSH requires Linux, digest-pinned images, absolute bind/credential paths and an approved high control port." }));
                return services;
            }
            services.AddSingleton(new SharedSshGatewayOptions(connector, runnerId!, Read("ClientImage"), Read("RelayImage"),
                Read("ServerHost"), serverPort, Read("PrivateKeyFile"), Read("KnownHostsFile"), Read("LocalStateDirectory"), Read("HostStateDirectory"),
                Enumerable.Range(capability.FirstPort, capability.LastPort - capability.FirstPort + 1).Where(capability.AllowsPort).ToArray()));
            services.AddSingleton<IPublicGatewayTransport, DockerSharedSshGateway>();
            services.AddSingleton<PublicGatewayAgent>();
            services.AddHostedService(provider => provider.GetRequiredService<PublicGatewayAgent>());
            return services;
        }
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
            Required("ServerHost"), configuration.GetValue("PublicGateway:ServerPort", 60999), Required("ServerName"),
            Required("CaFile"), Required("CertificateFile"), Required("KeyFile"), Required("TokenFile")));
        services.AddSingleton<DockerPublicGateway>();
        services.AddSingleton<IPublicGatewayTransport>(provider => provider.GetRequiredService<DockerPublicGateway>());
        services.AddSingleton<PublicGatewayAgent>();
        services.AddHostedService(provider => provider.GetRequiredService<PublicGatewayAgent>());
        return services;
    }
}
