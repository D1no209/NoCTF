using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using NoCTF.Runner.Composition;

namespace NoCTF.Tests.Unit.Application;

public class RunnerScoringTokenIssuerTests
{
    [Test]
    public async Task Issue_EmitsShortLivedScoringScopeToken()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RunnerScoring:SigningKey"] = "runner-signing-key-with-at-least-32-bytes!",
            ["RunnerScoring:Issuer"] = "NoCTF.Runner.Test",
            ["RunnerScoring:Audience"] = "NoCTF.ScoringInput.Test",
            ["RunnerScoring:LifetimeSeconds"] = "300"
        }).Build();

        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            new RunnerScoringTokenIssuer(configuration).Issue("runner-1", DateTimeOffset.UtcNow));

        await Assert.That(token.Issuer).IsEqualTo("NoCTF.Runner.Test");
        await Assert.That(token.Audiences.Single()).IsEqualTo("NoCTF.ScoringInput.Test");
        await Assert.That(token.Claims.Single(x => x.Type == "runner_id").Value).IsEqualTo("runner-1");
        await Assert.That(token.Claims.Single(x => x.Type == "scope").Value).IsEqualTo("scoring.write");
        await Assert.That(token.ValidTo - token.ValidFrom).IsLessThanOrEqualTo(TimeSpan.FromMinutes(5));
    }
}
