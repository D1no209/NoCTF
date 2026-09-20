namespace NoCTF.Application.Authentication.Sso;

public sealed record SsoFlowRecord(
    Guid Id,
    Guid ProviderId,
    NoCTF.Domain.Identity.SsoProtocol Protocol,
    SsoFlowIntent Intent,
    string BrowserIdHash,
    SsoFlowState State,
    string CorrelationToken,
    string ProviderFingerprint,
    string ReturnPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    Guid? UserId = null,
    int? TokenVersion = null,
    string? Nonce = null,
    string? PkceVerifier = null,
    string? ServiceUrl = null,
    string? ProcessingToken = null,
    SsoExternalIdentity? ExternalIdentity = null,
    SsoFailureCode? FailureCode = null);

public enum SsoFlowClaimState
{
    Claimed,
    NotFound,
    InvalidCorrelation,
    AlreadyProcessed,
    DependencyUnavailable
}

public sealed record SsoFlowClaimResult(
    SsoFlowClaimState State,
    SsoFlowRecord? Flow = null,
    string? ProcessingToken = null);

public enum SsoFlowReadState
{
    Available,
    NotFound,
    InvalidCorrelation,
    DependencyUnavailable
}

public sealed record SsoFlowReadResult(
    SsoFlowReadState State,
    SsoFlowRecord? Flow = null);

public interface ISsoFlowStore
{
    Task<bool> CreateAsync(SsoFlowRecord flow, CancellationToken cancellationToken);

    Task<SsoFlowClaimResult> ClaimCallbackAsync(
        string correlationToken,
        Guid providerId,
        string browserIdHash,
        CancellationToken cancellationToken);

    Task<bool> SaveAuthenticatedAsync(
        Guid flowId,
        string processingToken,
        SsoExternalIdentity identity,
        CancellationToken cancellationToken);

    Task<bool> SaveFailureAsync(
        Guid flowId,
        string processingToken,
        SsoFailureCode failure,
        CancellationToken cancellationToken);

    Task<SsoFlowReadResult> ReadAsync(
        Guid flowId,
        string browserIdHash,
        CancellationToken cancellationToken);

    Task<SsoFlowReadResult> ConsumeAuthenticatedAsync(
        Guid flowId,
        string browserIdHash,
        CancellationToken cancellationToken);
}

public sealed class SsoFlowDependencyException(Exception? inner = null)
    : Exception("The SSO flow store is unavailable.", inner);
