namespace NoCTF.Domain.Platform;

public enum RuntimeAccessRoute : short { Direct, Gateway }
public enum PublicAccessState : short { Disabled, Pending, Ready, Unavailable, Revoking, Unsupported }
public enum PublicAccessFailure : short
{
    GatewayDisabled,
    ConnectorOffline,
    PublicPortUnavailable,
    RuntimeBindingUnavailable,
    UnsupportedRuntimeKind,
    AccessDisplayUnsupported,
    GatewayCapacityExceeded,
    GatewayIdentityRejected,
    GatewaySafetyCheckFailed,
    GatewayReconciliationPending
}
