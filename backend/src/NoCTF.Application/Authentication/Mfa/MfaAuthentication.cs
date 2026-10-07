using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Application.Authentication.Mfa;

public enum AuthenticationState : short { Authenticated, MfaRequired, EnrollmentRequired }
public sealed record MfaBrowserCredential(Guid ChallengeId, string Secret);
public sealed record MfaFlowView(Guid Id, MfaChallengePurpose Purpose, DateTimeOffset ExpiresAt, int RemainingAttempts,
    string UserName, bool RecoveryAvailable, string ReturnPath, string? Secret = null, string? ProvisioningUri = null, bool PrimaryAuthenticationRequired = false);
public sealed record MfaAccountSnapshot(AuthenticatedUser User, bool Mandated, bool Required, Guid PolicyStamp,
    Guid? CredentialId, int RecoveryCodesRemaining, bool RecoveryMailAvailable);
public sealed record PrimaryAuthentication(AuthenticatedUser User, AuthenticationMethod Method, DateTimeOffset AuthenticatedAt,
    OidcMfaProof? OidcProof = null, string ReturnPath = "/", Guid? ProviderId = null);
public sealed record MfaAuthenticationResult(AuthenticatedUser User, AuthenticationContext Context, string ReturnPath, IReadOnlyList<string>? RecoveryCodes = null);
public sealed record MfaContextValidationRequest(string Key, Guid UserId, int TokenVersion, AuthenticationContext? Authentication);
public sealed record AuthenticationCompletion(AuthenticationState State, MfaAuthenticationResult? Authentication = null,
    IssuedAccessToken? Access = null, IssuedRefreshToken? Refresh = null, MfaFlowView? Flow = null, MfaBrowserCredential? Browser = null);

public interface IMfaAuthenticationStore
{
    Task<MfaAccountSnapshot?> ReadAccountAsync(Guid userId, CancellationToken ct);
    Task<MfaFailure?> ValidateContextAsync(Guid userId, int tokenVersion, AuthenticationContext? context, CancellationToken ct);
    Task<bool> IsOidcProofCurrentAsync(OidcMfaProof proof, CancellationToken ct);
    Task<(MfaFlowView Flow, MfaBrowserCredential Browser)> CreateFlowAsync(PrimaryAuthentication primary, MfaChallengePurpose purpose, CancellationToken ct);
    Task<OperationResult<MfaFlowView, MfaFailure>> ReadFlowAsync(MfaBrowserCredential browser, CancellationToken ct);
    Task CancelFlowAsync(MfaBrowserCredential browser, CancellationToken ct);
    Task<OperationResult<MfaAuthenticationResult, MfaFailure>> VerifyAsync(MfaBrowserCredential browser, MfaVerification verification, CancellationToken ct);
    Task<OperationResult<MfaFlowView, MfaFailure>> BeginEnrollmentAsync(MfaBrowserCredential browser, CancellationToken ct);
    Task<OperationResult<MfaAuthenticationResult, MfaFailure>> ConfirmEnrollmentAsync(MfaBrowserCredential browser, string code, CancellationToken ct, bool accountManagement = false);
    Task<OperationResult<MfaFlowView, MfaFailure>> ApplyRecoveryPrimaryAsync(MfaBrowserCredential browser, PrimaryAuthentication primary, CancellationToken ct);
    Task<IReadOnlyDictionary<string, MfaFailure?>> ValidateContextsAsync(IReadOnlyList<MfaContextValidationRequest> requests, CancellationToken ct);
}

public sealed class CompleteAuthentication(IMfaAuthenticationStore store, IAccessTokenIssuer issuer, TimeProvider clock)
{
    public async Task<OperationResult<AuthenticationCompletion, MfaFailure>> ExecuteAsync(PrimaryAuthentication primary, CancellationToken ct = default, MfaBrowserCredential? recovery = null)
    {
        var snapshot = await store.ReadAccountAsync(primary.User.Id, ct);
        if (snapshot is null || snapshot.User.TokenVersion != primary.User.TokenVersion) return OperationResult<AuthenticationCompletion, MfaFailure>.Failure(MfaFailure.AccountUnavailable, "Account is unavailable.");
        if (recovery is not null)
        {
            var resumed = await store.ApplyRecoveryPrimaryAsync(recovery, primary, ct);
            if (resumed.Succeeded) return OperationResult<AuthenticationCompletion, MfaFailure>.Success(new(AuthenticationState.EnrollmentRequired, Flow: resumed.Value, Browser: recovery));
            if (resumed.FailureCode == MfaFailure.InvalidRecoveryGrant) return OperationResult<AuthenticationCompletion, MfaFailure>.Failure(MfaFailure.InvalidRecoveryGrant, "Recovery authorization does not match this account.");
        }
        var context = new AuthenticationContext(primary.Method, primary.AuthenticatedAt);
        if (primary.OidcProof is { } proof && await store.IsOidcProofCurrentAsync(proof, ct))
            context = context with { MfaSource = MfaSource.Oidc, MfaAuthenticatedAt = proof.AuthenticatedAt, ProviderId = proof.ProviderId, TrustPolicyId = proof.TrustPolicyId };
        if (!snapshot.Required || context.HasMfa)
            return OperationResult<AuthenticationCompletion, MfaFailure>.Success(Issue(new(snapshot.User, context, primary.ReturnPath)));
        var purpose = snapshot.CredentialId is null ? MfaChallengePurpose.Enrollment : MfaChallengePurpose.Login;
        var (flow, browser) = await store.CreateFlowAsync(primary with { User = snapshot.User }, purpose, ct);
        return OperationResult<AuthenticationCompletion, MfaFailure>.Success(new(
            purpose == MfaChallengePurpose.Login ? AuthenticationState.MfaRequired : AuthenticationState.EnrollmentRequired,
            Flow: flow, Browser: browser));
    }

    public AuthenticationCompletion Issue(MfaAuthenticationResult authentication)
    {
        var now = clock.GetUtcNow();
        return new(AuthenticationState.Authenticated, authentication,
            issuer.Issue(authentication.User, authentication.Context, now), issuer.IssueRefresh(authentication.User, authentication.Context));
    }
}
