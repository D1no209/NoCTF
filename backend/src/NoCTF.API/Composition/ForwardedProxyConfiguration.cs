using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace NoCTF.API.Composition;

internal static class ForwardedProxyConfiguration
{
    public static IServiceCollection AddNoCtfForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var knownNetworks = configuration
            .GetSection("ForwardedHeaders:KnownNetworks")
            .Get<string[]>() ?? [];
        var knownProxies = configuration
            .GetSection("ForwardedHeaders:KnownProxies")
            .Get<string[]>() ?? [];
        var allowedHosts = configuration
            .GetSection("ForwardedHeaders:AllowedHosts")
            .Get<string[]>() ?? [];

        var parsedNetworks = knownNetworks.Select(ParseNetwork).ToArray();
        var parsedProxies = knownProxies.Select(ParseAddress).ToArray();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var network in parsedNetworks)
                options.KnownIPNetworks.Add(network);
            foreach (var proxy in parsedProxies)
                options.KnownProxies.Add(proxy);
            foreach (var host in allowedHosts)
                options.AllowedHosts.Add(host);
        });
        return services;
    }

    private static System.Net.IPNetwork ParseNetwork(string value)
    {
        if (!System.Net.IPNetwork.TryParse(value, out var network))
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks contains invalid CIDR '{value}'.");
        return network;
    }

    private static IPAddress ParseAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address))
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownProxies contains invalid address '{value}'.");
        return address;
    }
}
