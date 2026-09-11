namespace NoCTF.Application.Admission;

public enum HumanVerificationProvider
{
    None,
    Cap,
    Turnstile
}

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

    public Uri CapApiEndpoint()
    {
        var serverUrl = Cap.ServerUrl.TrimEnd('/') + "/";
        return new Uri(
            new Uri(serverUrl, UriKind.Absolute),
            $"{Uri.EscapeDataString(Cap.SiteKey)}/");
    }

    private bool IsValidCap(bool development)
    {
        if (Cap is null
            || string.IsNullOrWhiteSpace(Cap.SiteKey)
            || string.IsNullOrWhiteSpace(Cap.Secret)
            || Cap.SiteKey != Cap.SiteKey.Trim()
            || Cap.ServerUrl != Cap.ServerUrl.Trim()
            || !Uri.TryCreate(Cap.ServerUrl, UriKind.Absolute, out var serverUri)
            || serverUri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(serverUri.UserInfo)
            || !string.IsNullOrEmpty(serverUri.Query)
            || !string.IsNullOrEmpty(serverUri.Fragment))
            return false;

        return development || serverUri.Scheme == Uri.UriSchemeHttps;
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
    public string SiteKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

public sealed class TurnstileHumanVerificationOptions
{
    public string SiteKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string[] AllowedHostnames { get; set; } = [];
}
