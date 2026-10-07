namespace NoCTF.Domain.Identity.Mfa;

public enum MfaPolicy : short { Optional, RequirePrivileged, RequireAllHumanUsers }
public enum AuthenticationMethod : short { Password, Oidc, Cas, AdministratorIssued, Bot, Passkey }
public enum MfaSource : short { None, Totp, RecoveryCode, Oidc }

public sealed record AuthenticationContext(
    AuthenticationMethod Method,
    DateTimeOffset AuthenticatedAt,
    MfaSource MfaSource = MfaSource.None,
    DateTimeOffset? MfaAuthenticatedAt = null,
    Guid? CredentialId = null,
    Guid? ProviderId = null,
    Guid? TrustPolicyId = null,
    Guid? PrimaryCredentialId = null)
{
    public bool IsInteractive => Method is AuthenticationMethod.Password or AuthenticationMethod.Oidc or AuthenticationMethod.Cas or AuthenticationMethod.Passkey;
    public bool IsLocalMfa => MfaSource is MfaSource.Totp or MfaSource.RecoveryCode;
    public bool HasMfa => MfaSource != MfaSource.None;
    public DateTimeOffset? MfaDeadline => MfaAuthenticatedAt?.AddDays(30);
    public bool IsWellFormed => Enum.IsDefined(Method) && Enum.IsDefined(MfaSource)
        && (Method == AuthenticationMethod.Passkey ? PrimaryCredentialId is not null : PrimaryCredentialId is null)
        && (MfaSource switch
        {
            MfaSource.None => MfaAuthenticatedAt is null && CredentialId is null && ProviderId is null && TrustPolicyId is null,
            MfaSource.Totp or MfaSource.RecoveryCode => IsInteractive && MfaAuthenticatedAt is not null && CredentialId is not null
                && ProviderId is null && TrustPolicyId is null,
            MfaSource.Oidc => Method == AuthenticationMethod.Oidc && MfaAuthenticatedAt is not null
                && ProviderId is not null && TrustPolicyId is not null && CredentialId is null,
            _ => false
        });
}

public static class MfaRequirements
{
    public static bool IsMandated(UserKind kind, UserRole role, bool accountRequired, MfaPolicy policy, bool competitionPrivileged) =>
        kind == UserKind.Human && (accountRequired || policy == MfaPolicy.RequireAllHumanUsers
            || policy == MfaPolicy.RequirePrivileged && (role is UserRole.Administrator or UserRole.Organizer || competitionPrivileged));

    public static bool IsRequired(UserKind kind, UserRole role, bool accountRequired, MfaPolicy policy, bool competitionPrivileged, bool hasTotp) =>
        kind == UserKind.Human && (hasTotp || IsMandated(kind, role, accountRequired, policy, competitionPrivileged));
}
