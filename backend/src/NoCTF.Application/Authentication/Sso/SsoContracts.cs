using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.Sso;

public static class SsoRules
{
    public const int MaximumProviders = 16;
    public const int MaximumProviderNameLength = 100;
    public const int MaximumUrlLength = 2048;
    public const int MaximumIdentityNamespaceLength = 512;
    public const int MaximumSubjectLength = 255;
    public const int MaximumClientIdLength = 512;
    public const int MaximumSecretLength = 4096;
    public const int MaximumScopes = 16;
    public const int MaximumScopeLength = 128;
    public const int MaximumAllowedHosts = 16;
    public const int MaximumAttributeNameLength = 128;
    public const int MinimumTimeoutSeconds = 1;
    public const int MaximumTimeoutSeconds = 30;
    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResultLifetime = TimeSpan.FromMinutes(2);
}

public enum SsoFlowIntent : short
{
    Login,
    Bind,
    AdministratorTest
}

public enum SsoFlowState : short
{
    Pending,
    Processing,
    Authenticated,
    Failed,
    Consumed
}

public enum SsoFailureCode
{
    SsoDisabled,
    ProviderNotFound,
    ProviderUnavailable,
    ProviderChanged,
    FlowExpired,
    InvalidCorrelation,
    AuthenticationFailed,
    IdentityNotLinked,
    IdentityAlreadyLinked,
    AccountAlreadyLinked,
    AccountUnavailable,
    ReauthenticationRequired
}

public sealed record SsoPublicProvider(
    Guid Id,
    string Name,
    string? IconUrl,
    SsoProtocol Protocol,
    bool CanLogin,
    bool CanBind);

public sealed record SsoExternalIdentity(
    Guid ProviderId,
    SsoProtocol Protocol,
    string IdentityNamespace,
    string Subject,
    string? DisplayName);
