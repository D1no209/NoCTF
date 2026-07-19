using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication.Ports;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Application;

public class JwtIssuerTests
{
    [Test]
    public async Task Issue_ContainsValidatedIssuerAudienceAndSecurityClaims()
    {
        var config = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:SigningKey"] = "test-signing-key-with-at-least-32-bytes!",
            ["Authentication:Issuer"] = "NoCTF.Test",
            ["Authentication:Audience"] = "NoCTF.Api.Test",
            ["Authentication:AccessTokenMinutes"] = "5"
        });
        var user = new AuthenticatedUser(Guid.NewGuid(), "alice", "Admin", 7);
        var issued = new JwtIssuer(config).Issue(user);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);

        await Assert.That(token.Issuer).IsEqualTo("NoCTF.Test");
        await Assert.That(token.Audiences.Single()).IsEqualTo("NoCTF.Api.Test");
        await Assert.That(token.Claims.Single(x => x.Type == "token_version").Value).IsEqualTo("7");
        await Assert.That(token.Id).IsNotNull();
        await Assert.That(issued.ExpiresAt).IsGreaterThan(DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task Issue_RejectsShortSigningKey()
    {
        var config = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:SigningKey"] = "too-short"
        });

        await Assert.That(() => new JwtIssuer(config)).Throws<InvalidOperationException>();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
