using Microsoft.AspNetCore.Http;

namespace NoCTF.API.Endpoints.Authentication;

internal static class RefreshCookie
{
    private const string SecureName = "__Secure-noctf_refresh";
    private const string InsecureName = "noctf_refresh";
    private const string CookiePath = "/api/v1/auth";

    public static string Name(IConfiguration configuration)
        => IsSecure(configuration) ? SecureName : InsecureName;

    public static CookieOptions Options(IConfiguration configuration)
        => new()
        {
            HttpOnly = true,
            Secure = IsSecure(configuration),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            MaxAge = TimeSpan.FromDays(30)
        };

    public static CookieOptions DeleteOptions(IConfiguration configuration)
        => new()
        {
            HttpOnly = true,
            Secure = IsSecure(configuration),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath
        };

    private static bool IsSecure(IConfiguration configuration)
        => configuration.GetValue("Authentication:RefreshCookieSecure", true);
}
