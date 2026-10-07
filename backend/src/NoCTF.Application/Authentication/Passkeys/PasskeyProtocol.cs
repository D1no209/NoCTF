using NoCTF.Application.Common;
using NoCTF.Domain.Identity.Passkeys;

namespace NoCTF.Application.Authentication.Passkeys;

public enum PasskeyFailure : short
{
    Unavailable, InvalidOrigin, FlowExpired, InvalidBrowser, InvalidCredential, AlreadyRegistered,
    AccountUnavailable, PrimaryAuthenticationRequired, StepUpRequired, CredentialLimitReached, InvalidName,
    CredentialNotFound, PolicyChanged, DependencyUnavailable, RateLimited
}
public sealed record PasskeyUserIdentity(Guid Id, string Name);
public sealed record PasskeyProtocolOptions(string OptionsJson, string State);
public sealed record VerifiedPasskey(byte[] CredentialId, byte[] PublicKey, uint SignCount, bool IsUserVerified,
    bool IsBackupEligible, bool IsBackedUp, byte[] AttestationObject, byte[] ClientDataJson,
    IReadOnlyList<PasskeyTransport> Transports);
public sealed record PasskeyAttestation(Guid UserId, VerifiedPasskey Credential);
public sealed record PasskeyAssertion(Guid UserId, VerifiedPasskey Credential);

// JSON and binary fields here are opaque WebAuthn protocol boundaries.
public interface IPasskeyProtocol
{
    Task<PasskeyProtocolOptions> CreateRegistrationOptionsAsync(PasskeyUserIdentity user, string origin, CancellationToken ct);
    Task<PasskeyProtocolOptions> CreateLoginOptionsAsync(string origin, CancellationToken ct);
    Task<OperationResult<PasskeyAttestation, PasskeyFailure>> VerifyRegistrationAsync(string state, string credentialJson, string origin, CancellationToken ct);
    Task<OperationResult<PasskeyAssertion, PasskeyFailure>> VerifyLoginAsync(string state, string credentialJson, string origin, CancellationToken ct);
}
