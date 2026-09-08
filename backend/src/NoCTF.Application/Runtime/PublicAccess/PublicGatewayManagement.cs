using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Runtime.PublicAccess;

public sealed record ReconcilePublicGateway(string RunnerId) : IRunnerNodeMessage;
public sealed record PublicGatewayConfiguration(PublicGatewayPolicy Policy, PublicGatewayCapability? Capability);
public sealed record PublicGatewaySaveResult(PublicGatewayConfiguration? Configuration, IReadOnlyList<string> Errors);
public sealed record PublicGatewayConnectorStatus(string ConnectorId, string PolicyHash, DateTimeOffset ValidUntil,
    IReadOnlyList<PublicRuntimeStatus> Runtimes, NoCTF.Domain.Platform.PublicAccessFailure? Failure);

public interface IPublicGatewayPolicyStore
{
    Task<PublicGatewayPolicy> GetAsync(CancellationToken ct);
    Task SaveAsync(PublicGatewayPolicy policy, DateTimeOffset now, CancellationToken ct);
    Task InvalidateAsync(CancellationToken ct);
}

public interface IPublicGatewayStatusStore
{
    Task<PublicRuntimeStatus?> GetAsync(Guid runtimeId, CancellationToken ct);
    Task SetAsync(PublicRuntimeStatus status, CancellationToken ct);
    Task RemoveAsync(Guid runtimeId, CancellationToken ct);
    Task<PublicGatewayConnectorStatus?> GetConnectorAsync(string connectorId, CancellationToken ct);
    Task SetConnectorAsync(PublicGatewayConnectorStatus status, CancellationToken ct);
}

public sealed class ManagePublicGateway(IPublicGatewayPolicyStore store, PublicGatewayCapability? capability = null)
{
    public async Task<PublicGatewayConfiguration> GetAsync(CancellationToken ct) => new(await store.GetAsync(ct), capability);
    public async Task<PublicGatewaySaveResult> SaveAsync(PublicGatewayPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        var errors = PublicGatewayPolicyRules.Validate(policy, capability);
        if (errors.Count > 0) return new(null, errors);
        await store.SaveAsync(policy, now, ct);
        return new(new(policy, capability), []);
    }
}
