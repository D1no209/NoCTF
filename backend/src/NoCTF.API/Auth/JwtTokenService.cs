using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Core;

namespace NoCTF.API.Auth;

public class JwtTokenService(IConfiguration configuration)
{
    public const string SessionStartedAtClaim = "session_started_at";

    public string GenerateToken(User user, DateTimeOffset? sessionStartedAt = null)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var secret = jwtSettings.GetValue<string>("Secret")!;
        var issuer = jwtSettings.GetValue<string>("Issuer") ?? "NoCTF";
        var audience = jwtSettings.GetValue<string>("Audience") ?? "NoCTF";
        var expiryMinutes = Math.Clamp(jwtSettings.GetValue<int>("ExpiryMinutes", 60), 1, 24 * 60);
        var sessionStart = sessionStartedAt ?? DateTimeOffset.UtcNow;
        if (!IsSessionActive(sessionStart))
            throw new InvalidOperationException("Authentication session has expired.");
        var absoluteExpiry = sessionStart.Add(GetMaximumSessionDuration());
        var tokenExpiry = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);
        if (tokenExpiry > absoluteExpiry)
            tokenExpiry = absoluteExpiry;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.UserName),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim("role", user.Role.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("token_version", user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(
                SessionStartedAtClaim,
                sessionStart.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: tokenExpiry.UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool IsSessionActive(DateTimeOffset sessionStartedAt)
    {
        var now = DateTimeOffset.UtcNow;
        return sessionStartedAt <= now.AddMinutes(1) &&
               sessionStartedAt.Add(GetMaximumSessionDuration()) > now;
    }

    private TimeSpan GetMaximumSessionDuration()
        => TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetSection("JwtSettings").GetValue("MaxSessionMinutes", 24 * 60),
            5,
            7 * 24 * 60));
}
