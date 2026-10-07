using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Application.Authentication.Passkeys;

public sealed record PasskeyBrowserCredential(Guid CeremonyId, string Secret);
public sealed record PasskeyStartedCeremony(string OptionsJson, DateTimeOffset ExpiresAt, PasskeyBrowserCredential Browser);
public sealed record PasskeyAccountCredential(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool IsBackedUp, bool IsBackupEligible);
public sealed record PasskeyAccountStatus(bool Available, bool NeedsLocalProof, bool RecentPrimaryAuthentication, int MaximumCredentials, IReadOnlyList<PasskeyAccountCredential> Credentials);
public sealed record PasskeyPrimaryAuthentication(AuthenticatedUser User, Guid CredentialId, string ReturnPath);
public interface IPasskeyStore
{
    bool AvailableForOrigin(string origin);
    Task<OperationResult<PasskeyStartedCeremony, PasskeyFailure>> BeginLoginAsync(string origin, string returnPath, CancellationToken ct);
    Task<OperationResult<PasskeyPrimaryAuthentication, PasskeyFailure>> FinishLoginAsync(PasskeyBrowserCredential browser, string credentialJson, string origin, CancellationToken ct);
    Task<OperationResult<PasskeyAccountStatus, PasskeyFailure>> ReadAccountAsync(MfaActor actor, string origin, CancellationToken ct);
    Task<OperationResult<PasskeyStartedCeremony, PasskeyFailure>> BeginRegistrationAsync(MfaActor actor, MfaBrowserCredential? proof, string name, string origin, CancellationToken ct);
    Task<OperationResult<PasskeyAccountCredential, PasskeyFailure>> FinishRegistrationAsync(MfaActor actor, PasskeyBrowserCredential browser, string credentialJson, string origin, CancellationToken ct);
    Task<OperationResult<PasskeyAccountCredential, PasskeyFailure>> RenameAsync(MfaActor actor, MfaBrowserCredential? proof, Guid credentialId, string name, CancellationToken ct);
    Task<OperationResult<bool, PasskeyFailure>> RemoveAsync(MfaActor actor, MfaBrowserCredential? proof, Guid credentialId, CancellationToken ct);
    Task CancelAsync(PasskeyBrowserCredential browser, CancellationToken ct);
}

public sealed class AuthenticateWithPasskey(IPasskeyStore store, CompleteAuthentication complete, TimeProvider clock,
    NoCTF.Application.Authentication.Privacy.IAccountActivityRecorder? activities = null)
{
    public async Task<OperationResult<AuthenticationCompletion, PasskeyFailure>> ExecuteAsync(PasskeyBrowserCredential browser,
        string credentialJson, string origin, MfaBrowserCredential? recovery, CancellationToken ct)
    {
        var verified = await store.FinishLoginAsync(browser, credentialJson, origin, ct);
        if (!verified.Succeeded) return OperationResult<AuthenticationCompletion, PasskeyFailure>.Failure(verified.FailureCode!.Value, "Passkey authentication failed.");
        var primary = verified.Value!;
        var result = await complete.ExecuteAsync(new(primary.User, AuthenticationMethod.Passkey, clock.GetUtcNow(),
            ReturnPath: primary.ReturnPath, CredentialId: primary.CredentialId), ct, recovery);
        if (result.Succeeded && result.Value!.State == AuthenticationState.Authenticated && activities is not null)
            await activities.RecordLoginAsync(primary.User.Id, clock.GetUtcNow(), ct);
        return result.Succeeded ? OperationResult<AuthenticationCompletion, PasskeyFailure>.Success(result.Value!)
            : OperationResult<AuthenticationCompletion, PasskeyFailure>.Failure(result.FailureCode == MfaFailure.InvalidRecoveryGrant
                ? PasskeyFailure.AccountUnavailable : PasskeyFailure.DependencyUnavailable, "Authentication could not be completed.");
    }
}
