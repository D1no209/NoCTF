using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Authentication;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerScoringTokenIssuerTests
{
    [Test]
    public async Task Awd_checker_token_binds_resource_version_sequence_and_callback_window()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RunnerScoring:SigningKey"] = "runner-scoring-test-key-with-at-least-32-bytes",
                ["RunnerScoring:Issuer"] = "issuer",
                ["RunnerScoring:Audience"] = "audience"
            })
            .Build();
        var issuer = new RunnerScoringTokenIssuer(configuration);
        var runtimeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var issuedAt = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var deadline = issuedAt.AddMinutes(1);

        var encoded = issuer.IssueAwdChecker(new AwdCheckerTokenRequest(
            "runner-a", runtimeId, 3, 4, 5, deadline, issuedAt));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(encoded);
        await Assert.That(token.Audiences).Contains("audience");
        await Assert.That(token.Claims.Single(claim => claim.Type == "token_type").Value)
            .IsEqualTo("internal");
        await Assert.That(token.Claims.Single(claim => claim.Type == "resource").Value)
            .IsEqualTo($"runtime:{runtimeId:D}");
        await Assert.That(token.Claims.Single(claim => claim.Type == "processing_version").Value)
            .IsEqualTo("5");
        await Assert.That(token.Claims.Single(claim => claim.Type == "checker_sequence").Value)
            .IsEqualTo("4");
        await Assert.That(token.ValidTo)
            .IsEqualTo(deadline.AddHours(24).UtcDateTime);
    }
}
