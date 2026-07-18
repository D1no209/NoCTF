using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication.Ports;

namespace NoCTF.Infrastructure.Authentication;

public sealed class JwtIssuer(IConfiguration configuration) : IAccessTokenIssuer
{
    private readonly byte[] key = Encoding.UTF8.GetBytes(
        configuration["Authentication:SigningKey"] ?? throw new InvalidOperationException("Authentication:SigningKey is required."));
    private readonly TimeSpan lifetime = TimeSpan.FromMinutes(
        configuration.GetValue("Authentication:AccessTokenMinutes", 15));

    public IssuedAccessToken Issue(AuthenticatedUser user)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims:
            [
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName),
                new(ClaimTypes.Role, user.Role),
                new("token_version", user.TokenVersion.ToString())
            ],
            expires: expires.UtcDateTime,
            signingCredentials: credentials);
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
