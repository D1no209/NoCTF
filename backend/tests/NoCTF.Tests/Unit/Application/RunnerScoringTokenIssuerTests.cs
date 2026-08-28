using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerScoringTokenIssuerTests
{
    [Test]
    public async Task Awd_checker_token_binds_runtime_fact_and_callback_window()
    {
        var issuer = CreateIssuer();
        var runtimeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var gameplayFactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var issuedAt = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var deadline = issuedAt.AddMinutes(1);

        var encoded = issuer.IssueAwdChecker(new AwdCheckerTokenRequest(
            "runner-a", runtimeId, gameplayFactId, deadline, issuedAt));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(encoded);
        await Assert.That(token.Audiences).Contains("audience");
        await Assert.That(token.Claims.Single(claim => claim.Type == "token_type").Value)
            .IsEqualTo("internal");
        await Assert.That(token.Claims.Single(claim => claim.Type == "resource").Value)
            .IsEqualTo($"runtime:{runtimeId:D}");
        await Assert.That(token.Claims.Single(claim => claim.Type == "gameplay_fact_id").Value)
            .IsEqualTo(gameplayFactId.ToString("D"));
        await Assert.That(token.Claims.Any(claim => claim.Type == "processing_version"))
            .IsFalse();
        await Assert.That(token.ValidTo)
            .IsEqualTo(deadline.AddHours(24).UtcDateTime);
    }

    [Test]
    public async Task Awdp_callback_token_binds_submission_and_target()
    {
        var issuer = CreateIssuer();
        var gameplayFactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var runtimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var issuedAt = DateTimeOffset.Parse("2026-07-24T00:00:00Z");

        var encoded = issuer.IssueAwdpFixResult(new AwdpFixResultTokenRequest(
            "runner-a", gameplayFactId, runtimeId, issuedAt.AddMinutes(2), issuedAt));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(encoded);
        await Assert.That(token.Claims.Single(claim => claim.Type == "permission").Value)
            .IsEqualTo("awdp:fix-result:write");
        await Assert.That(token.Claims.Single(claim => claim.Type == "resource").Value)
            .IsEqualTo($"gameplay-fact:{gameplayFactId:D}:runtime:{runtimeId:D}");
        await Assert.That(token.Claims.Any(claim => claim.Type == "generation"))
            .IsFalse();
        await Assert.That(token.Claims.Any(claim => claim.Type == "runtime_processing_version"))
            .IsFalse();
    }

    [Test]
    public async Task Awdp_archive_token_grants_read_only_access_to_one_gameplay_fact_upload()
    {
        var issuer = CreateIssuer();
        var uploadId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var gameplayFactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var issuedAt = DateTimeOffset.Parse("2026-07-24T00:00:00Z");

        var encoded = issuer.IssueFixArchiveRead(
            "runner-a",
            uploadId,
            gameplayFactId,
            issuedAt);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(encoded);
        await Assert.That(token.Claims.Single(claim => claim.Type == "permission").Value)
            .IsEqualTo("awdp:fix-archive:read");
        await Assert.That(token.Claims.Single(claim => claim.Type == "patch_upload_id").Value)
            .IsEqualTo(uploadId.ToString("D"));
        await Assert.That(token.Claims.Single(claim => claim.Type == "gameplay_fact_id").Value)
            .IsEqualTo(gameplayFactId.ToString("D"));
        await Assert.That(token.ValidTo).IsEqualTo(issuedAt.AddMinutes(5).UtcDateTime);
    }

    private static RunnerScoringTokenIssuer CreateIssuer() => new(Options.Create(
        new RunnerScoringOptions
        {
            SigningKey = "runner-scoring-test-key-with-at-least-32-bytes",
            Issuer = "issuer",
            Audience = "audience"
        }));
}
