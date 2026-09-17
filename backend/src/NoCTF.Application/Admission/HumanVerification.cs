using NoCTF.Domain.Platform;

namespace NoCTF.Application.Admission;

public enum HumanVerificationAction
{
    Login,
    Registration,
    Runtime,
    Evaluation
}

public enum HumanVerificationResult
{
    Verified,
    Rejected,
    Unavailable
}

public sealed record HumanVerificationAttempt(
    string Token,
    HumanVerificationAction Action,
    string? RemoteIpAddress);

public interface IHumanVerificationVerifier
{
    ValueTask<HumanVerificationResult> VerifyAsync(
        HumanVerificationRuntimeConfiguration configuration,
        HumanVerificationAttempt attempt,
        CancellationToken cancellationToken);
}

public sealed class HumanVerificationOptions
{
    public const string SectionName = "HumanVerification";

    public HumanVerificationProvider Provider { get; set; }
    public CapHumanVerificationOptions Cap { get; set; } = new();
    public TurnstileHumanVerificationOptions Turnstile { get; set; } = new();

    public bool IsValid(bool development) => Provider switch
    {
        HumanVerificationProvider.None => true,
        HumanVerificationProvider.Cap => IsValidCap(development),
        HumanVerificationProvider.Turnstile => IsValidTurnstile(development),
        _ => false
    };

    public Uri CapApiEndpoint() => BuildCapApiEndpoint(Cap.ServerUrl);

    public Uri CapBackendApiEndpoint() =>
        BuildCapApiEndpoint(CapBackendServerRoot().AbsoluteUri);

    public Uri CapBackendServerRoot()
    {
        var serverUrl = string.IsNullOrWhiteSpace(Cap.BackendServerUrl)
            ? Cap.ServerUrl
            : Cap.BackendServerUrl;
        return new Uri(serverUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private bool IsValidCap(bool development)
    {
        if (Cap is null
            || string.IsNullOrWhiteSpace(Cap.SiteKey)
            || string.IsNullOrWhiteSpace(Cap.Secret)
            || Cap.SiteKey != Cap.SiteKey.Trim()
            || Cap.ServerUrl != Cap.ServerUrl.Trim()
            || !TryServiceEndpoint(Cap.ServerUrl, out var serverUri)
            || (!string.IsNullOrWhiteSpace(Cap.BackendServerUrl)
                && (Cap.BackendServerUrl != Cap.BackendServerUrl.Trim()
                    || !TryServiceEndpoint(Cap.BackendServerUrl, out _))))
            return false;

        return development || serverUri.Scheme == Uri.UriSchemeHttps;
    }

    private Uri BuildCapApiEndpoint(string serverUrl) => new(
        new Uri(serverUrl.TrimEnd('/') + "/", UriKind.Absolute),
        $"{Uri.EscapeDataString(Cap.SiteKey)}/");

    private static bool TryServiceEndpoint(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            uri = null!;
            return false;
        }
        uri = parsed;
        return true;
    }

    private bool IsValidTurnstile(bool development)
    {
        if (Turnstile is null
            || string.IsNullOrWhiteSpace(Turnstile.SiteKey)
            || string.IsNullOrWhiteSpace(Turnstile.Secret)
            || Turnstile.SiteKey != Turnstile.SiteKey.Trim()
            || Turnstile.AllowedHostnames is not { Length: > 0 })
            return false;

        return Turnstile.AllowedHostnames.All(hostname =>
            !string.IsNullOrWhiteSpace(hostname)
            && hostname == hostname.Trim()
            && Uri.CheckHostName(hostname) != UriHostNameType.Unknown
            && (development || !IsLoopbackHost(hostname)));
    }

    private static bool IsLoopbackHost(string hostname) =>
        string.Equals(hostname, "localhost", StringComparison.OrdinalIgnoreCase)
        || (System.Net.IPAddress.TryParse(hostname, out var address)
            && System.Net.IPAddress.IsLoopback(address));
}

public sealed class CapHumanVerificationOptions
{
    public string ServerUrl { get; set; } = string.Empty;
    public string BackendServerUrl { get; set; } = string.Empty;
    public string SiteKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

public sealed class TurnstileHumanVerificationOptions
{
    public string SiteKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string[] AllowedHostnames { get; set; } = [];
}
