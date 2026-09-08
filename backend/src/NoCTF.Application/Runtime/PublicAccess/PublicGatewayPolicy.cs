using System.Globalization;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.PublicAccess;

public sealed record PublicGatewayPolicy(
    bool Enabled,
    string ConnectorId,
    string PublicOrigin,
    IReadOnlyList<string> DirectOrigins,
    string PublicRuntimeHost,
    string? DirectRuntimeHostOverride,
    int MaxPublishedPorts)
{
    public static PublicGatewayPolicy Disabled { get; } = new(false, "", "", [], "", null, 0);
    public string Fingerprint() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this)));
}

/// <summary>Read-only capabilities approved at deployment, never supplied by a browser.</summary>
public sealed record PublicGatewayCapability(
    string ConnectorId,
    string RunnerId,
    IReadOnlyList<string> ApprovedOrigins,
    int FirstPort,
    int LastPort,
    IReadOnlyList<int> ReservedPorts,
    int MaximumPorts,
    bool NamespaceIsolationAvailable,
    string? ConfigurationError = null)
{
    public bool AllowsPort(int port) => port >= FirstPort && port <= LastPort && !ReservedPorts.Contains(port);
}

public sealed record PublicEndpointStatus(int ContainerPort, int HostPort, PublicAccessState State, PublicAccessFailure? Failure);
public sealed record PublicRuntimeStatus(Guid RuntimeId, string ConnectorId, string RunnerId,
    DateTimeOffset ValidUntil, IReadOnlyList<PublicEndpointStatus> Endpoints, PublicAccessFailure? Failure = null);
public sealed record RuntimeAccessProjection(RuntimeAccessRoute Route, PublicAccessState State,
    PublicAccessFailure? Failure, IReadOnlyList<string> Urls, IReadOnlyList<PublicEndpointStatus> Endpoints);

public static class PublicGatewayPolicyRules
{
    public static string? Origin(string value, bool requireHttps = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || requireHttps && uri.Scheme != "https"
            || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return null;
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    public static bool Host(string value) => value.Length is > 0 and <= 253
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-')
        && Uri.CheckHostName(value) is UriHostNameType.Dns or UriHostNameType.IPv4;

    public static IReadOnlyList<string> Validate(PublicGatewayPolicy policy, PublicGatewayCapability? capability)
    {
        var errors = new List<string>();
        if (!policy.Enabled && policy.ConnectorId.Length == 0 && policy.PublicOrigin.Length == 0
            && policy.DirectOrigins.Count == 0 && policy.PublicRuntimeHost.Length == 0
            && string.IsNullOrEmpty(policy.DirectRuntimeHostOverride) && policy.MaxPublishedPorts == 0) return errors;
        var origin = Origin(policy.PublicOrigin, requireHttps: true);
        if (origin is null) errors.Add("PublicOrigin must be an HTTPS origin without credentials, path, query or fragment.");
        if (!Host(policy.PublicRuntimeHost)) errors.Add("PublicRuntimeHost must be an IPv4 address or DNS host name.");
        if (policy.DirectRuntimeHostOverride is { Length: > 0 } host && !Host(host))
            errors.Add("DirectRuntimeHostOverride must be an IPv4 address or DNS host name.");
        var direct = policy.DirectOrigins.Select(value => Origin(value)).ToArray();
        if (direct.Length is 0 or > 16 || direct.Any(value => value is null) || direct.Distinct().Count() != direct.Length
            || direct.Contains(origin)) errors.Add("DirectOrigins must contain 1–16 distinct origins, excluding PublicOrigin.");
        if (capability is null || policy.ConnectorId != capability.ConnectorId)
            errors.Add("The connector is not approved by this deployment.");
        else
        {
            if (!capability.ApprovedOrigins.Select(value => Origin(value, true)).Contains(origin))
                errors.Add("PublicOrigin is not in the deployment's approved origin list.");
            if (policy.MaxPublishedPorts <= 0 || policy.MaxPublishedPorts > capability.MaximumPorts)
                errors.Add("MaxPublishedPorts exceeds the approved connector capacity.");
            if (policy.Enabled && !capability.NamespaceIsolationAvailable)
                errors.Add("The connector has not passed namespace isolation safety checks.");
        }
        return errors;
    }

    /// <summary>Unknown origins are rejected, never reclassified using the client's IP.</summary>
    public static RuntimeAccessRoute? Route(PublicGatewayPolicy policy, string effectiveOrigin)
    {
        if (policy.PublicOrigin.Length == 0 && !policy.Enabled) return RuntimeAccessRoute.Direct;
        var origin = Origin(effectiveOrigin);
        if (origin is null) return null;
        if (origin == Origin(policy.PublicOrigin, true)) return RuntimeAccessRoute.Gateway;
        return policy.DirectOrigins.Any(value => Origin(value) == origin) ? RuntimeAccessRoute.Direct : null;
    }

    public static bool Supports(RuntimeInstanceView runtime, GameMode mode, string runnerId, DateTimeOffset now) =>
        runtime.Provider == RuntimeProvider.Docker && runtime.RuntimeKind == RuntimeKind.Container
        && runtime.RunnerId == runnerId && runtime.State == RuntimeState.Running && runtime.ExpiresAt > now
        && (mode == GameMode.Ctf && runtime.Purpose is RuntimePurpose.Player or RuntimePurpose.Practice
            || mode == GameMode.Awdp && runtime.Purpose is RuntimePurpose.AwdpAttack or RuntimePurpose.Practice);

    /// <summary>Call only after the existing user/team/challenge authorization has succeeded.</summary>
    public static RuntimeAccessProjection Project(PublicGatewayPolicy policy, RuntimeAccessRoute route,
        PublicGatewayCapability? capability, RuntimeInstanceView runtime, GameMode mode,
        IReadOnlyList<RuntimeUrlBinding> bindings, PublicRuntimeStatus? status, DateTimeOffset now)
    {
        if (route == RuntimeAccessRoute.Direct)
        {
            if (runtime.State != RuntimeState.Running) return new(route, PublicAccessState.Disabled, null, [], []);
            if (string.IsNullOrWhiteSpace(policy.DirectRuntimeHostOverride) || runtime.RuntimeKind != RuntimeKind.Container)
                return new(route, PublicAccessState.Disabled, null, runtime.Urls, []);
            var displays = new List<string>();
            foreach (var binding in bindings)
            {
                var matches = (runtime.PublishedPorts ?? []).Where(port => port.ServiceName is null
                    && binding.ServiceName is null && port.ContainerPort == binding.ContainerPort).ToArray();
                if (matches.Length != 1)
                    return new(route, PublicAccessState.Unavailable, PublicAccessFailure.RuntimeBindingUnavailable, [], []);
                displays.Add(binding.UrlTemplate.Replace("{HOST}", policy.DirectRuntimeHostOverride, StringComparison.Ordinal)
                    .Replace("{PORT}", matches[0].HostPort.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }
            return new(route, PublicAccessState.Disabled, null, displays, []);
        }
        RuntimeAccessProjection Unavailable(PublicAccessState state, PublicAccessFailure failure) => new(route, state, failure, [], []);
        if (!policy.Enabled) return Unavailable(PublicAccessState.Disabled, PublicAccessFailure.GatewayDisabled);
        if (capability?.NamespaceIsolationAvailable != true)
            return Unavailable(PublicAccessState.Unavailable, PublicAccessFailure.GatewaySafetyCheckFailed);
        if (capability is null || !Supports(runtime, mode, capability.RunnerId, now))
            return Unavailable(PublicAccessState.Unsupported, PublicAccessFailure.UnsupportedRuntimeKind);
        if (status is null || status.ValidUntil <= now || status.RuntimeId != runtime.Id
            || status.ConnectorId != policy.ConnectorId || status.RunnerId != runtime.RunnerId)
            return Unavailable(PublicAccessState.Unavailable, PublicAccessFailure.ConnectorOffline);
        if (status.Failure is { } statusFailure)
            return Unavailable(PublicAccessState.Unavailable, statusFailure);
        var ports = runtime.PublishedPorts ?? [];
        var urls = new List<string>();
        var endpoints = new List<PublicEndpointStatus>();
        foreach (var binding in bindings)
        {
            if (binding.ServiceName is not null || binding.ContainerPort is not int containerPort)
                return Unavailable(PublicAccessState.Unsupported, PublicAccessFailure.RuntimeBindingUnavailable);
            var matches = ports.Where(port => port.ServiceName is null && port.ContainerPort == containerPort).ToArray();
            if (matches.Length != 1 || !capability.AllowsPort(matches[0].HostPort))
                return Unavailable(PublicAccessState.Unavailable, PublicAccessFailure.PublicPortUnavailable);
            if (!binding.UrlTemplate.Contains("{HOST}", StringComparison.Ordinal)
                || !binding.UrlTemplate.Contains("{PORT}", StringComparison.Ordinal))
                return Unavailable(PublicAccessState.Unsupported, PublicAccessFailure.AccessDisplayUnsupported);
            var match = matches[0];
            var states = status.Endpoints.Where(item => item.ContainerPort == containerPort && item.HostPort == match.HostPort).Take(2).ToArray();
            if (states.Length > 1) return Unavailable(PublicAccessState.Unavailable, PublicAccessFailure.RuntimeBindingUnavailable);
            var state = states.FirstOrDefault() ?? new(containerPort, match.HostPort, PublicAccessState.Pending, PublicAccessFailure.GatewayReconciliationPending);
            if (!endpoints.Contains(state)) endpoints.Add(state);
            if (state.State == PublicAccessState.Ready)
                urls.Add(binding.UrlTemplate.Replace("{HOST}", policy.PublicRuntimeHost, StringComparison.Ordinal)
                    .Replace("{PORT}", match.HostPort.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }
        var complete = endpoints.Count > 0 && endpoints.All(item => item.State == PublicAccessState.Ready);
        return new(route, complete ? PublicAccessState.Ready : PublicAccessState.Pending,
            complete ? null : PublicAccessFailure.GatewayReconciliationPending, urls, endpoints);
    }
}
