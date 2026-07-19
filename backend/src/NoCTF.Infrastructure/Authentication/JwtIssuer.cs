using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication.Ports;

namespace NoCTF.Infrastructure.Authentication;

public sealed class JwtIssuer(IConfiguration configuration) : IAccessTokenIssuer
{
    private readonly string issuer = configuration["Authentication:Issuer"] ?? "NoCTF";
    private readonly string audience = configuration["Authentication:Audience"] ?? "NoCTF.Api";
    private readonly byte[] key = ReadKey(configuration["Authentication:SigningKey"]);
    private readonly TimeSpan lifetime = TimeSpan.FromMinutes(
        configuration.GetValue("Authentication:AccessTokenMinutes", 15));

    public IssuedAccessToken Issue(AuthenticatedUser user)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName),
                new(ClaimTypes.Role, user.Role),
                new("token_version", user.TokenVersion.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ],
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static byte[] ReadKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < 32)
            throw new InvalidOperationException("Authentication:SigningKey must contain at least 32 UTF-8 bytes.");
        return Encoding.UTF8.GetBytes(value);
    }
}
