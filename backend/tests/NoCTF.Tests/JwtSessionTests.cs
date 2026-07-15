using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using NoCTF.API.Auth;
using NoCTF.Core;

namespace NoCTF.Tests;

public class JwtSessionTests
{
    [Fact]
    public void RefreshGeneration_PreservesAbsoluteSessionStart()
    {
        var service = CreateService(maxSessionMinutes: 120);
        var sessionStart = DateTimeOffset.UtcNow.AddMinutes(-30);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            service.GenerateToken(CreateUser(), sessionStart));

        Assert.Equal(
            sessionStart.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            token.Claims.Single(claim => claim.Type == JwtTokenService.SessionStartedAtClaim).Value);
    }

    [Fact]
    public void RefreshGeneration_RejectsExpiredAbsoluteSession()
    {
        var service = CreateService(maxSessionMinutes: 60);

        Assert.Throws<InvalidOperationException>(() =>
            service.GenerateToken(CreateUser(), DateTimeOffset.UtcNow.AddMinutes(-61)));
    }

    private static JwtTokenService CreateService(int maxSessionMinutes)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = new string('s', 64),
                ["JwtSettings:Issuer"] = "NoCTF.Tests",
                ["JwtSettings:Audience"] = "NoCTF.Tests",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:MaxSessionMinutes"] = maxSessionMinutes.ToString()
            })
            .Build();
        return new JwtTokenService(configuration);
    }

    private static User CreateUser()
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = "session-user",
            Email = "session@example.test",
            Role = UserRole.Organizer,
            TokenVersion = 3
        };
}
