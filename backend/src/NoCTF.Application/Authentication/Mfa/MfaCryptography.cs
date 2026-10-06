namespace NoCTF.Application.Authentication.Mfa;

public interface IMfaCryptography
{
    string GenerateSecret();
    bool VerifyTotp(string secret, string code, DateTimeOffset now, out long matchedStep);
    IReadOnlyList<string> GenerateRecoveryCodes();
    byte[]? HashRecoveryCode(string code);
    string GenerateBrowserSecret();
    string HashBrowserSecret(string secret);
}

public enum MfaVerificationMethod : short { Totp, RecoveryCode }
public sealed record MfaVerification(MfaVerificationMethod Method, string Code);
public enum MfaFailure : short
{
    FlowExpired, InvalidBrowser, InvalidCode, CodeAlreadyUsed, AttemptsExceeded, RateLimited,
    AccountUnavailable, PolicyChanged, NotApplicable, AlreadyEnrolled, NotEnrolled,
    PolicyRequiresMfa, PrimaryAuthenticationRequired, StepUpRequired, LocalMfaRequired,
    EmailUnverified, EmailNotConfigured, InvalidRecoveryGrant, DependencyUnavailable, DelegationNotAllowed
}
