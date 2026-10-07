using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Application.Authentication.Passkeys;

public sealed class PasskeyOptions
{
    public string ServerDomain { get; set; } = string.Empty;
    public string[] AllowedOrigins { get; set; } = [];
    public int MaximumCredentials { get; set; } = 10;
    public int CeremonyLifetimeSeconds { get; set; } = 180;
    public bool IsConfigured => ServerDomain.Length is > 0 and <= 253 && !ServerDomain.Contains(':')
        && !ServerDomain.Contains('/') && AllowedOrigins.Length > 0;
    public bool IsValid(bool allowLocalHttp) => MaximumCredentials is >= 1 and <= 20 && CeremonyLifetimeSeconds is >= 60 and <= 300
        && ((!IsConfigured && ServerDomain.Length == 0 && AllowedOrigins.Length == 0) || IsConfigured && ServerDomain == ServerDomain.ToLowerInvariant() && Uri.CheckHostName(ServerDomain) == UriHostNameType.Dns
        && AllowedOrigins.All(value => ValidOrigin(value, allowLocalHttp)));
    private bool ValidOrigin(string value, bool allowLocalHttp) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0
        && (uri.Scheme == "https" || allowLocalHttp && uri.Scheme == "http" && uri.Host == "localhost")
        && (uri.Host == ServerDomain || uri.Host.EndsWith("." + ServerDomain, StringComparison.Ordinal));
    public bool AllowsOrigin(string origin) => IsConfigured && AllowedOrigins.Any(value =>
        string.Equals(value.TrimEnd('/'), origin, StringComparison.Ordinal));
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        ServerDomain + "\n" + string.Join("\n", AllowedOrigins.Order(StringComparer.Ordinal)) + "\n" + MaximumCredentials + "\n" + CeremonyLifetimeSeconds)));
}
