using Microsoft.AspNetCore.Http;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class RefreshHttpOptions
{
    public bool RefreshCookieSecure { get; init; } = true;

    public string[] RefreshAllowedOrigins { get; init; } = [];
}

internal static class RefreshCookie
{
    private const string SecureName = "__Secure-noctf_refresh";
    private const string InsecureName = "noctf_refresh";
    private const string CookiePath = "/api/v1/auth";

    public static string Name(RefreshHttpOptions options)
        => options.RefreshCookieSecure ? SecureName : InsecureName;

    public static CookieOptions Options(RefreshHttpOptions options)
        => new()
        {
            HttpOnly = true,
            Secure = options.RefreshCookieSecure,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            MaxAge = TimeSpan.FromDays(30)
        };

    public static CookieOptions DeleteOptions(RefreshHttpOptions options)
        => new()
        {
            HttpOnly = true,
            Secure = options.RefreshCookieSecure,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath
        };

}
