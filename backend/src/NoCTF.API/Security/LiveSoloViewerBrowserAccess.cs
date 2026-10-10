using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace NoCTF.API.Security;

public sealed record LiveSoloViewerBrowserAccessMetadata;
public sealed class LiveSoloViewerBrowserAccess(IDataProtectionProvider protection, TimeProvider clock, IWebHostEnvironment environment)
{
    private readonly IDataProtector protector = protection.CreateProtector("NoCTF.LiveSoloViewerBrowserAccess.v1");
    private static string Prefix(Guid competitionId, Guid matchId) => $"/api/v1/competitions/{competitionId:D}/live-solo/matches/{matchId:D}/program";
    private string Name(string prefix) => (environment.IsDevelopment() ? "NoCTF.LiveSoloViewer." : "__Secure-NoCTF.LiveSoloViewer.")
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prefix)))[..24];
    private CookieOptions Options(string prefix, DateTimeOffset? expires = null) => new() {
        HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict, Path = prefix, Expires = expires, IsEssential = true };
    public Guid LeaseId(HttpContext context, Guid competitionId, Guid matchId) => GrantFor(context, Prefix(competitionId, matchId))?.LeaseId ?? Guid.Empty;
    public void Write(HttpContext context, Guid competitionId, Guid matchId, Guid leaseId)
    {
        var prefix = Prefix(competitionId, matchId);
        var authorization = context.Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : GrantFor(context, prefix)?.AccessToken;
        var expires = clock.GetUtcNow().AddMinutes(30);
        context.Response.Cookies.Append(Name(prefix), protector.Protect(JsonSerializer.Serialize(new Grant(prefix, leaseId, token, expires))), Options(prefix, expires));
    }
    public void Clear(HttpContext context, Guid competitionId, Guid matchId)
    { var prefix = Prefix(competitionId, matchId); context.Response.Cookies.Delete(Name(prefix), Options(prefix)); }
    public string? ReadAccessToken(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Headers.ContainsKey("Authorization")
            || context.GetEndpoint()?.Metadata.GetMetadata<LiveSoloViewerBrowserAccessMetadata>() is null) return null;
        if (!Guid.TryParse(context.Request.RouteValues["competitionId"]?.ToString(), out var competitionId)
            || !Guid.TryParse(context.Request.RouteValues["matchId"]?.ToString(), out var matchId)) return null;
        return GrantFor(context, Prefix(competitionId, matchId))?.AccessToken;
    }
    private Grant? GrantFor(HttpContext context, string prefix)
    {
        if (!context.Request.Path.StartsWithSegments(prefix) || !context.Request.Cookies.TryGetValue(Name(prefix), out var cookie)) return null;
        try { var grant = JsonSerializer.Deserialize<Grant>(protector.Unprotect(cookie)); return grant?.Prefix == prefix && grant.ExpiresAt > clock.GetUtcNow() ? grant : null; }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return null; }
    }
    private sealed record Grant(string Prefix, Guid LeaseId, string? AccessToken, DateTimeOffset ExpiresAt);
}
