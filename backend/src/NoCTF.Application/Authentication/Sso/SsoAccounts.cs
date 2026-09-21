using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.Sso;

public sealed record SsoBindingView(
    Guid ProviderId,
    string ProviderName,
    string? ProviderIconUrl,
    SsoProtocol Protocol,
    string IdentityNamespace,
    string Subject,
    DateTimeOffset BoundAt);

public enum SsoBindState
{
    Bound,
    AccountUnavailable,
    AccountAlreadyLinked,
    IdentityAlreadyLinked,
    ProviderChanged
}

public sealed record SsoBindResult(SsoBindState State, SsoBindingView? Binding = null);

public enum SsoUnbindState
{
    Unbound,
    NotLinked,
    AccountUnavailable
}

public enum AdminSsoUnbindState
{
    Unbound,
    UserNotFound,
    NotLinked
}

public enum SsoExternalAccountLookupState
{
    Available,
    NotLinked,
    AccountUnavailable
}

public sealed record SsoExternalAccountLookup(
    SsoExternalAccountLookupState State,
    AuthenticatedUser? User = null);

public interface ISsoAccountStore
{
    Task<SsoBindingView?> GetBindingAsync(Guid userId, CancellationToken cancellationToken);

    Task<SsoExternalAccountLookup> FindByExternalIdentityAsync(
        SsoExternalIdentity identity,
        CancellationToken cancellationToken);

    Task<SsoBindResult> BindAsync(
        Guid userId,
        int tokenVersion,
        SsoExternalIdentity identity,
        string providerName,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SsoUnbindState> UnbindAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<AdminSsoUnbindState> UnbindAsAdministratorAsync(
        Guid userId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record SsoLoginSession(
    Guid UserId,
    string UserName,
    UserRole Role,
    bool EmailVerified,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    string ReturnPath);

public sealed class GetSsoBinding(
    ISsoAccountStore accounts,
    ISsoConfigurationStore configuration)
{
    public async Task<(SsoBindingView? Binding, IReadOnlyList<SsoPublicProvider> Providers)> ExecuteAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var binding = await accounts.GetBindingAsync(userId, ct);
        var settings = await configuration.GetAsync(ct);
        var providers = !settings.Enabled
            ? []
            : settings.Providers
                .Where(provider => provider.Enabled && provider.AllowBinding)
                .Select(provider => new SsoPublicProvider(
                    provider.Id,
                    provider.Name,
                    provider.IconUrl,
                    provider.Protocol,
                    provider.AllowLogin,
                    provider.AllowBinding))
                .ToArray();
        return (binding, providers);
    }
}

public sealed class BeginSsoBinding(
    IUserAuthenticationStore users,
    ICredentialWorkAdmission admission,
    BeginSsoFlow begin)
{
    public async Task<OperationResult<BeginSsoFlowResult, SsoFailureCode>> ExecuteAsync(
        Guid userId,
        Guid providerId,
        string password,
        string browserIdHash,
        CancellationToken ct = default)
    {
        await using var lease = await admission.AcquireAsync(userId.ToString("N"), ct);
        ct = lease.Token;
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || user.Kind != UserKind.Human)
            return Failure(SsoFailureCode.AccountUnavailable);
        if (!await users.VerifyPasswordAsync(userId, password, ct))
            return Failure(SsoFailureCode.ReauthenticationRequired);
        user = await users.FindByIdAsync(userId, ct);
        return user is null
            ? Failure(SsoFailureCode.AccountUnavailable)
            : await begin.ExecuteAsync(new(
                providerId,
                SsoFlowIntent.Bind,
                browserIdHash,
                "/",
                userId,
                user.TokenVersion), ct);
    }

    private static OperationResult<BeginSsoFlowResult, SsoFailureCode> Failure(
        SsoFailureCode code) =>
        OperationResult<BeginSsoFlowResult, SsoFailureCode>.Failure(
            code,
            "The SSO binding flow could not be started.");
}

public sealed class CompleteSsoLogin(
    ISsoFlowStore flows,
    ISsoAccountStore accounts,
    ISsoProviderRuntimeReader providers,
    IAccessTokenIssuer issuer,
    IAccountActivityRecorder activities,
    TimeProvider clock)
{
    public async Task<OperationResult<SsoLoginSession, SsoFailureCode>> ExecuteAsync(
        Guid flowId,
        string browserIdHash,
        CancellationToken ct = default)
    {
        var consumed = await flows.ConsumeAuthenticatedAsync(flowId, browserIdHash, ct);
        if (consumed.State == SsoFlowReadState.DependencyUnavailable)
            return Failure(SsoFailureCode.ProviderUnavailable);
        if (consumed.State != SsoFlowReadState.Available
            || consumed.Flow is not { Intent: SsoFlowIntent.Login, ExternalIdentity: not null } flow)
            return Failure(SsoFailureCode.FlowExpired);
        var provider = await providers.FindAsync(flow.ProviderId, ct);
        if (provider is null || !provider.GlobalEnabled || !provider.Enabled
            || !provider.AllowLogin
            || !string.Equals(provider.Fingerprint, flow.ProviderFingerprint, StringComparison.Ordinal))
            return Failure(SsoFailureCode.ProviderChanged);
        var account = await accounts.FindByExternalIdentityAsync(flow.ExternalIdentity, ct);
        if (account.State != SsoExternalAccountLookupState.Available || account.User is null)
        {
            await activities.RecordSsoAsync(null, flow.ProviderId, false, clock.GetUtcNow(), ct);
            return Failure(account.State == SsoExternalAccountLookupState.NotLinked
                ? SsoFailureCode.IdentityNotLinked
                : SsoFailureCode.AccountUnavailable);
        }
        var user = account.User;
        var now = clock.GetUtcNow();
        var access = issuer.Issue(user, now);
        var refresh = issuer.IssueRefresh(user);
        await activities.RecordSsoAsync(user.Id, flow.ProviderId, true, now, ct);
        return OperationResult<SsoLoginSession, SsoFailureCode>.Success(new(
            user.Id,
            user.UserName,
            user.Role,
            user.EmailVerified,
            access.Token,
            access.ExpiresAt,
            refresh.Token,
            flow.ReturnPath));
    }

    private static OperationResult<SsoLoginSession, SsoFailureCode> Failure(
        SsoFailureCode code) =>
        OperationResult<SsoLoginSession, SsoFailureCode>.Failure(
            code,
            "The SSO login could not be completed.");
}

public sealed class CompleteSsoBinding(
    ISsoFlowStore flows,
    ISsoAccountStore accounts,
    ISsoProviderRuntimeReader providers,
    TimeProvider clock)
{
    public async Task<OperationResult<SsoBindingView, SsoFailureCode>> ExecuteAsync(
        Guid flowId,
        Guid userId,
        string browserIdHash,
        CancellationToken ct = default)
    {
        var consumed = await flows.ConsumeAuthenticatedAsync(flowId, browserIdHash, ct);
        if (consumed.State == SsoFlowReadState.DependencyUnavailable)
            return Failure(SsoFailureCode.ProviderUnavailable);
        if (consumed.State != SsoFlowReadState.Available
            || consumed.Flow is not
            {
                Intent: SsoFlowIntent.Bind,
                UserId: not null,
                TokenVersion: not null,
                ExternalIdentity: not null
            } flow
            || flow.UserId != userId)
            return Failure(SsoFailureCode.FlowExpired);
        var provider = await providers.FindAsync(flow.ProviderId, ct);
        if (provider is null || !provider.GlobalEnabled || !provider.Enabled
            || !provider.AllowBinding
            || !string.Equals(provider.Fingerprint, flow.ProviderFingerprint, StringComparison.Ordinal))
            return Failure(SsoFailureCode.ProviderChanged);
        var result = await accounts.BindAsync(
            userId,
            flow.TokenVersion.Value,
            flow.ExternalIdentity,
            provider.ProviderName,
            clock.GetUtcNow(),
            ct);
        return result.State switch
        {
            SsoBindState.Bound => OperationResult<SsoBindingView, SsoFailureCode>.Success(
                result.Binding!),
            SsoBindState.AccountAlreadyLinked => Failure(SsoFailureCode.AccountAlreadyLinked),
            SsoBindState.IdentityAlreadyLinked => Failure(SsoFailureCode.IdentityAlreadyLinked),
            SsoBindState.ProviderChanged => Failure(SsoFailureCode.ProviderChanged),
            _ => Failure(SsoFailureCode.AccountUnavailable)
        };
    }

    private static OperationResult<SsoBindingView, SsoFailureCode> Failure(SsoFailureCode code) =>
        OperationResult<SsoBindingView, SsoFailureCode>.Failure(
            code,
            "The external identity could not be linked.");
}

public sealed class UnbindSsoIdentity(
    IUserAuthenticationStore users,
    ICredentialWorkAdmission admission,
    ISsoAccountStore accounts,
    TimeProvider clock)
{
    public async Task<OperationResult<SsoFailureCode>> ExecuteAsync(
        Guid userId,
        string password,
        CancellationToken ct = default)
    {
        await using var lease = await admission.AcquireAsync(userId.ToString("N"), ct);
        ct = lease.Token;
        if (!await users.VerifyPasswordAsync(userId, password, ct))
            return OperationResult<SsoFailureCode>.Failure(
                SsoFailureCode.ReauthenticationRequired,
                "The current password is incorrect.");
        return await accounts.UnbindAsync(userId, clock.GetUtcNow(), ct) switch
        {
            SsoUnbindState.Unbound => OperationResult<SsoFailureCode>.Success(),
            SsoUnbindState.NotLinked => OperationResult<SsoFailureCode>.Failure(
                SsoFailureCode.IdentityNotLinked,
                "The account has no external identity binding."),
            _ => OperationResult<SsoFailureCode>.Failure(
                SsoFailureCode.AccountUnavailable,
                "The account is unavailable.")
        };
    }
}

public sealed class AdministrativelyUnbindSsoIdentity(
    ISsoAccountStore accounts,
    TimeProvider clock)
{
    public Task<AdminSsoUnbindState> ExecuteAsync(
        Guid userId,
        Guid actorUserId,
        CancellationToken ct = default) =>
        accounts.UnbindAsAdministratorAsync(
            userId,
            actorUserId,
            clock.GetUtcNow(),
            ct);
}
