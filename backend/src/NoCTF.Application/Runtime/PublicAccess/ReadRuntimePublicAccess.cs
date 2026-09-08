using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Platform;

namespace NoCTF.Application.Runtime.PublicAccess;

public sealed class ReadRuntimePublicAccess(IPublicGatewayPolicyStore policies, IPublicGatewayStatusStore statuses,
    TimeProvider clock, PublicGatewayCapability? capability = null)
{
    public async Task<RuntimeAccessRoute?> RouteAsync(string effectiveOrigin, CancellationToken ct)
    {
        if (capability is null) return RuntimeAccessRoute.Direct;
        return ResolveRoute(await policies.GetAsync(ct), effectiveOrigin);
    }
    private RuntimeAccessRoute? ResolveRoute(PublicGatewayPolicy policy, string origin)
    {
        if (policy.PublicOrigin.Length == 0 && capability?.ApprovedOrigins.Any(value =>
                PublicGatewayPolicyRules.Origin(value, true) == PublicGatewayPolicyRules.Origin(origin)) == true)
            return RuntimeAccessRoute.Gateway;
        return PublicGatewayPolicyRules.Route(policy, origin);
    }
    public async Task<RuntimeAccessProjection?> ExecuteAsync(RuntimeInstanceView authorizedRuntime, string effectiveOrigin, CancellationToken ct)
    {
        if (capability is null)
            return PublicGatewayPolicyRules.Project(PublicGatewayPolicy.Disabled, RuntimeAccessRoute.Direct, null,
                authorizedRuntime, authorizedRuntime.Mode ?? GameMode.Ctf, [], null, clock.GetUtcNow());
        var policy = await policies.GetAsync(ct);
        var route = ResolveRoute(policy, effectiveOrigin);
        if (route is null) return null;
        var status = route == RuntimeAccessRoute.Gateway && policy.Enabled
            ? await statuses.GetAsync(authorizedRuntime.Id, ct) : null;
        return PublicGatewayPolicyRules.Project(policy, route.Value, capability, authorizedRuntime,
            authorizedRuntime.Mode ?? GameMode.Ctf, authorizedRuntime.AccessBindings ?? [], status, clock.GetUtcNow());
    }
}
