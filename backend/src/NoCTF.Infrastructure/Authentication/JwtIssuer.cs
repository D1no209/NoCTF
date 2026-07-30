using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.Infrastructure.Authentication;

public sealed class JwtIssuer(IConfiguration configuration) : IAccessTokenIssuer
{
    private readonly string issuer = configuration["Authentication:Issuer"] ?? "NoCTF";
    private readonly string audience = configuration["Authentication:Audience"] ?? "NoCTF.Api";
    private readonly string refreshAudience = configuration["Authentication:RefreshAudience"] ?? "NoCTF.Refresh";
    private readonly byte[] key = ReadKey(configuration["Authentication:SigningKey"]);
    private readonly TimeSpan lifetime = TimeSpan.FromMinutes(
        configuration.GetValue("Authentication:AccessTokenMinutes", 15));

    public IssuedAccessToken Issue(
        AuthenticatedUser user,
        DateTimeOffset now,
        TimeSpan? requestedLifetime = null)
    {
        var expires = now.Add(requestedLifetime ?? lifetime);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName),
                new(ClaimTypes.Role, user.Role.ToString()),
                new("user_kind", user.Kind.ToString()),
                new("token_version", user.TokenVersion.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new("token_type", "access")
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public IssuedRefreshToken IssueRefresh(AuthenticatedUser user)
    {
        var expires = DateTimeOffset.UtcNow.AddDays(30);
        var now = DateTimeOffset.UtcNow;
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: refreshAudience,
            claims:
            [
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new("token_type", "refresh"),
                new("token_version", user.TokenVersion.ToString())
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public RefreshTokenPrincipal? ValidateRefresh(string token)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = refreshAudience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);
            if (!string.Equals(principal.FindFirstValue("token_type"), "refresh", StringComparison.Ordinal))
                return null;
            if (string.IsNullOrWhiteSpace(principal.FindFirstValue(JwtRegisteredClaimNames.Jti))
                || !long.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Iat), out _))
                return null;
            var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(subject, out var userId)
                || !int.TryParse(principal.FindFirstValue("token_version"), out var tokenVersion))
                return null;
            return new(userId, tokenVersion);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static byte[] ReadKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < 32)
            throw new InvalidOperationException("Authentication:SigningKey must contain at least 32 UTF-8 bytes.");
        return Encoding.UTF8.GetBytes(value);
    }
}
