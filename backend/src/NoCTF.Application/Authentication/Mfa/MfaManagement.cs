using NoCTF.Application.Common;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Application.Authentication.Mfa;

public sealed record MfaCreatedFlow(MfaFlowView Flow, MfaBrowserCredential Browser);
public sealed record MfaActor(Guid UserId, int TokenVersion, AuthenticationContext Authentication);
public sealed record MfaRecoveryGrant(Guid Id, Guid UserId, DateTimeOffset ExpiresAt);
public sealed record MfaChangeResult(Guid UserId, int TokenVersion);
public sealed record MfaProviderTrust(Guid ProviderId, string Name, OidcMfaTrust Trust);
public sealed record MfaPlatformConfiguration(MfaPolicy Policy, IReadOnlyList<MfaProviderTrust> Providers);

public interface IMfaManagementStore
{
    Task<OperationResult<MfaPlatformConfiguration, MfaFailure>> ReadConfigurationAsync(MfaActor actor, CancellationToken ct);
    Task<OperationResult<MfaCreatedFlow, MfaFailure>> BeginOwnEnrollmentAsync(MfaActor actor, CancellationToken ct);
    Task<OperationResult<MfaCreatedFlow, MfaFailure>> BeginStepUpAsync(MfaActor actor, MfaOperation operation, Guid? target, CancellationToken ct);
    Task<OperationResult<MfaFlowView, MfaFailure>> VerifyStepUpAsync(MfaBrowserCredential browser, MfaVerification verification, CancellationToken ct);
    Task<OperationResult<MfaCreatedFlow, MfaFailure>> BeginRebindAsync(MfaActor actor, MfaBrowserCredential proof, CancellationToken ct);
    Task<OperationResult<IReadOnlyList<string>, MfaFailure>> RegenerateRecoveryCodesAsync(MfaActor actor, MfaBrowserCredential proof, CancellationToken ct);
    Task<OperationResult<MfaChangeResult, MfaFailure>> DisableAsync(MfaActor actor, MfaBrowserCredential proof, CancellationToken ct);
    Task<OperationResult<MfaPolicy, MfaFailure>> ChangePolicyAsync(MfaActor actor, MfaBrowserCredential proof, MfaPolicy policy, CancellationToken ct);
    Task<OperationResult<MfaChangeResult, MfaFailure>> ChangeRequirementAsync(MfaActor actor, MfaBrowserCredential proof, Guid userId, bool required, CancellationToken ct);
    Task<OperationResult<MfaRecoveryGrant, MfaFailure>> GrantRecoveryAsync(MfaActor? actor, MfaBrowserCredential? proof, Guid userId, string reason, CancellationToken ct);
    Task<OperationResult<MfaCreatedFlow, MfaFailure>> ExchangeRecoveryAsync(string token, CancellationToken ct);
    Task<OperationResult<NoCTF.Application.Authentication.Account.AuthenticatedUser, MfaFailure>> ConsumeStepUpAsync(MfaActor actor, MfaBrowserCredential proof, MfaOperation operation, Guid? target, CancellationToken ct);
    Task<OperationResult<OidcMfaTrust, MfaFailure>> ChangeOidcTrustAsync(MfaActor actor, MfaBrowserCredential proof, Guid providerId, bool enabled, int maxAgeSeconds, IReadOnlyList<string> acr, IReadOnlyList<IReadOnlyList<string>> amr, CancellationToken ct);
}
