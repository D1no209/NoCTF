using NoCTF.Domain.Identity;
using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.Sso;

public sealed record OidcSsoRuntimeConfiguration(
    string Issuer,
    string DiscoveryUrl,
    string ClientId,
    string ClientSecret,
    IReadOnlyList<string> Scopes,
    bool ReadUserInfo,
    string DisplayNameClaim);

public sealed record CasSsoRuntimeConfiguration(
    string IdentityNamespace,
    string LoginUrl,
    string ServiceValidateUrl,
    string DisplayNameAttribute);

public sealed record SsoProviderRuntimeConfiguration(
    bool GlobalEnabled,
    string PublicBaseUrl,
    Guid ProviderId,
    string ProviderName,
    SsoProtocol Protocol,
    bool Enabled,
    bool AllowLogin,
    bool AllowBinding,
    int TimeoutSeconds,
    IReadOnlyList<string> AllowedHosts,
    string Fingerprint,
    OidcSsoRuntimeConfiguration? Oidc,
    CasSsoRuntimeConfiguration? Cas);

public interface ISsoProviderRuntimeReader
{
    Task<SsoProviderRuntimeConfiguration?> FindAsync(
        Guid providerId,
        CancellationToken cancellationToken);
}

public sealed record SsoAuthorizationRequest(
    Uri AuthorizationUrl,
    string? Nonce = null,
    string? PkceVerifier = null,
    string? ServiceUrl = null);

public interface ISsoProtocolAdapter
{
    SsoProtocol Protocol { get; }

    Task<SsoAuthorizationRequest> CreateAuthorizationAsync(
        SsoProviderRuntimeConfiguration provider,
        string correlationToken,
        CancellationToken cancellationToken);

    Task<OperationResult<SsoExternalIdentity, SsoFailureCode>> AuthenticateAsync(
        SsoProviderRuntimeConfiguration provider,
        SsoFlowRecord flow,
        string credential,
        CancellationToken cancellationToken);
}

public sealed record BeginSsoFlowCommand(
    Guid ProviderId,
    SsoFlowIntent Intent,
    string BrowserIdHash,
    string ReturnPath,
    Guid? UserId = null,
    int? TokenVersion = null);

public sealed record BeginSsoFlowResult(
    Guid FlowId,
    Uri AuthorizationUrl,
    DateTimeOffset ExpiresAt);

public sealed class BeginSsoFlow(
    ISsoProviderRuntimeReader providers,
    IEnumerable<ISsoProtocolAdapter> adapters,
    ISsoFlowStore flows,
    TimeProvider clock)
{
    public async Task<OperationResult<BeginSsoFlowResult, SsoFailureCode>> ExecuteAsync(
        BeginSsoFlowCommand command,
        CancellationToken ct = default)
    {
        var provider = await providers.FindAsync(command.ProviderId, ct);
        if (provider is null)
            return Failure(SsoFailureCode.ProviderNotFound, "The SSO provider was not found.");
        if (command.Intent != SsoFlowIntent.AdministratorTest && !provider.GlobalEnabled)
            return Failure(SsoFailureCode.SsoDisabled, "Single sign-on is disabled.");
        if (command.Intent != SsoFlowIntent.AdministratorTest && !provider.Enabled)
            return Failure(SsoFailureCode.ProviderUnavailable, "The SSO provider is disabled.");
        if (command.Intent == SsoFlowIntent.Login && !provider.AllowLogin
            || command.Intent == SsoFlowIntent.Bind && !provider.AllowBinding)
            return Failure(SsoFailureCode.ProviderUnavailable, "The SSO provider does not allow this operation.");
        if (command.Intent == SsoFlowIntent.Bind
            && (command.UserId is null || command.TokenVersion is null))
            return Failure(SsoFailureCode.AccountUnavailable, "The binding account is unavailable.");

        var adapter = adapters.SingleOrDefault(item => item.Protocol == provider.Protocol);
        if (adapter is null)
            return Failure(SsoFailureCode.ProviderUnavailable, "The SSO protocol is unavailable.");
        var now = clock.GetUtcNow();
        var flowId = Guid.CreateVersion7(now);
        var correlationToken = RandomToken();
        SsoAuthorizationRequest authorization;
        try
        {
            authorization = await adapter.CreateAuthorizationAsync(provider, correlationToken, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or InvalidOperationException)
        {
            return Failure(SsoFailureCode.ProviderUnavailable, "The SSO provider is unavailable.");
        }

        var expiresAt = now.Add(SsoRules.FlowLifetime);
        var flow = new SsoFlowRecord(
            flowId,
            provider.ProviderId,
            provider.Protocol,
            command.Intent,
            command.BrowserIdHash,
            SsoFlowState.Pending,
            correlationToken,
            provider.Fingerprint,
            NormalizeReturnPath(command.ReturnPath),
            now,
            expiresAt,
            command.UserId,
            command.TokenVersion,
            authorization.Nonce,
            authorization.PkceVerifier,
            authorization.ServiceUrl);
        try
        {
            if (!await flows.CreateAsync(flow, ct))
                return Failure(SsoFailureCode.AuthenticationFailed, "The SSO flow could not be created.");
        }
        catch (SsoFlowDependencyException)
        {
            return Failure(SsoFailureCode.ProviderUnavailable, "The SSO flow service is unavailable.");
        }
        return OperationResult<BeginSsoFlowResult, SsoFailureCode>.Success(
            new(flowId, authorization.AuthorizationUrl, expiresAt));
    }

    private static string NormalizeReturnPath(string value) =>
        value.StartsWith("/", StringComparison.Ordinal)
        && !value.StartsWith("//", StringComparison.Ordinal)
            ? value
            : "/";

    private static string RandomToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static OperationResult<BeginSsoFlowResult, SsoFailureCode> Failure(
        SsoFailureCode code,
        string message) =>
        OperationResult<BeginSsoFlowResult, SsoFailureCode>.Failure(code, message);
}

public sealed class CompleteSsoCallback(
    ISsoProviderRuntimeReader providers,
    IEnumerable<ISsoProtocolAdapter> adapters,
    ISsoFlowStore flows)
{
    public async Task<SsoCallbackCompletion> ExecuteAsync(
        Guid providerId,
        SsoProtocol protocol,
        string correlationToken,
        string browserIdHash,
        string credential,
        CancellationToken ct = default)
    {
        var claim = await flows.ClaimCallbackAsync(
            correlationToken, providerId, browserIdHash, ct);
        if (claim.State != SsoFlowClaimState.Claimed || claim.Flow is null)
            return new(null,
                claim.State == SsoFlowClaimState.DependencyUnavailable
                    ? SsoFailureCode.ProviderUnavailable
                    : SsoFailureCode.InvalidCorrelation);

        var flow = claim.Flow;
        var provider = await providers.FindAsync(providerId, ct);
        if (provider is null
            || flow.Protocol != protocol
            || provider.Protocol != protocol
            || !string.Equals(provider.Fingerprint, flow.ProviderFingerprint, StringComparison.Ordinal)
            || flow.Intent != SsoFlowIntent.AdministratorTest
                && (!provider.GlobalEnabled || !provider.Enabled))
        {
            await flows.SaveFailureAsync(
                flow.Id, claim.ProcessingToken!, SsoFailureCode.ProviderChanged, ct);
            return new(flow.Id, SsoFailureCode.ProviderChanged);
        }

        var adapter = adapters.SingleOrDefault(item => item.Protocol == protocol);
        if (adapter is null)
            return await FailAsync(flow, claim.ProcessingToken!,
                SsoFailureCode.ProviderUnavailable, "The SSO protocol is unavailable.", ct);
        OperationResult<SsoExternalIdentity, SsoFailureCode> authentication;
        try
        {
            authentication = await adapter.AuthenticateAsync(provider, flow, credential, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or InvalidOperationException)
        {
            return await FailAsync(flow, claim.ProcessingToken!,
                SsoFailureCode.ProviderUnavailable, "The SSO provider is unavailable.", ct);
        }
        if (!authentication.Succeeded)
            return await FailAsync(flow, claim.ProcessingToken!,
                authentication.FailureCode!.Value,
                authentication.ErrorMessage ?? "SSO authentication failed.", ct);
        if (!await flows.SaveAuthenticatedAsync(
                flow.Id, claim.ProcessingToken!, authentication.Value!, ct))
            return new(flow.Id, SsoFailureCode.AuthenticationFailed);
        return new(flow.Id, null);
    }

    private async Task<SsoCallbackCompletion> FailAsync(
        SsoFlowRecord flow,
        string processingToken,
        SsoFailureCode code,
        string message,
        CancellationToken ct)
    {
        await flows.SaveFailureAsync(flow.Id, processingToken, code, ct);
        return new(flow.Id, code);
    }
}

public sealed record SsoCallbackCompletion(Guid? FlowId, SsoFailureCode? FailureCode);
