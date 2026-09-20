using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Application;

public class JwtIssuerTests
{
    [Test]
    public async Task Issue_ContainsValidatedIssuerAudienceAndSecurityClaims()
    {
        var options = Options.Create(new AuthenticationTokenOptions
        {
            SigningKey = "test-signing-key-with-at-least-32-bytes!",
            Issuer = "NoCTF.Test",
            Audience = "NoCTF.Api.Test",
            AccessTokenMinutes = 5
        });
        var now = DateTimeOffset.UtcNow;
        var user = new AuthenticatedUser(
            Guid.NewGuid(), "alice", UserRole.Administrator, UserKind.Human, 7);
        var issued = new JwtIssuer(options, TimeProvider.System).Issue(user, now);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);

        await Assert.That(token.Issuer).IsEqualTo("NoCTF.Test");
        await Assert.That(token.Audiences.Single()).IsEqualTo("NoCTF.Api.Test");
        await Assert.That(token.Claims.Single(x => x.Type == "token_version").Value).IsEqualTo("7");
        await Assert.That(token.Claims.Single(x => x.Type == "user_kind").Value)
            .IsEqualTo("Human");
        await Assert.That(token.Id).IsNotNull();
        await Assert.That(issued.ExpiresAt).IsGreaterThan(DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task Issue_UsesRequestedBotLifetime()
    {
        var options = Options.Create(new AuthenticationTokenOptions
        {
            SigningKey = "test-signing-key-with-at-least-32-bytes!"
        });
        var now = DateTimeOffset.Parse("2026-07-30T10:00:00Z");
        var lifetime = TimeSpan.FromDays(365);
        var user = new AuthenticatedUser(
            Guid.NewGuid(),
            "gitops-bot",
            UserRole.Organizer,
            UserKind.Bot,
            3);

        var issued = new JwtIssuer(options, TimeProvider.System).Issue(user, now, lifetime);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);

        await Assert.That(issued.ExpiresAt).IsEqualTo(now.Add(lifetime));
        await Assert.That(token.Claims.Single(claim => claim.Type == "user_kind").Value)
            .IsEqualTo("Bot");
        await Assert.That(token.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value)
            .IsEqualTo("Organizer");
    }

    [Test]
    [Arguments(UserKind.Human, UserRole.User)]
    [Arguments(UserKind.Bot, UserRole.Organizer)]
    [Arguments(UserKind.Bot, UserRole.Administrator)]
    public async Task Issue_AlwaysOmitsLegacyImpersonationClaims(UserKind kind, UserRole role)
    {
        var options = Options.Create(new AuthenticationTokenOptions
        {
            SigningKey = "test-signing-key-with-at-least-32-bytes!"
        });
        var user = new AuthenticatedUser(
            Guid.NewGuid(),
            "target",
            role,
            kind,
            9,
            EmailVerified: false);

        var issued = new JwtIssuer(options, TimeProvider.System).Issue(
            user,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);

        await Assert.That(token.Claims.Any(claim =>
            claim.Type == LegacyAccessTokenClaims.Impersonation)).IsFalse();
        await Assert.That(token.Claims.Any(claim =>
            claim.Type == LegacyAccessTokenClaims.ImpersonatorId)).IsFalse();
        await Assert.That(token.Claims.Single(claim => claim.Type == "email_verified").Value)
            .IsEqualTo("false");
        await Assert.That(Guid.Parse(token.Id)).IsEqualTo(issued.JwtId);
    }

    [Test]
    public async Task Issue_RejectsShortSigningKey()
    {
        var options = Options.Create(new AuthenticationTokenOptions
        {
            SigningKey = "too-short"
        });

        await Assert.That(() => new JwtIssuer(options, TimeProvider.System))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task IssueRefresh_UsesDistinctAudienceAndThirtyDayLifetime()
    {
        var options = Options.Create(new AuthenticationTokenOptions
        {
            SigningKey = "test-signing-key-with-at-least-32-bytes!",
            Issuer = "NoCTF.Test",
            Audience = "NoCTF.Api.Test",
            RefreshAudience = "NoCTF.Refresh.Test"
        });
        var user = new AuthenticatedUser(
            Guid.NewGuid(), "alice", UserRole.Administrator, UserKind.Human, 7);
        var now = DateTimeOffset.Parse("2026-08-28T00:00:00Z");
        var issuer = new JwtIssuer(options, new FakeTimeProvider(now));
        var issued = issuer.IssueRefresh(user);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);

        await Assert.That(token.Audiences.Single()).IsEqualTo("NoCTF.Refresh.Test");
        await Assert.That(token.Claims.Single(x => x.Type == "token_type").Value).IsEqualTo("refresh");
        await Assert.That(token.Claims.Single(x => x.Type == "token_version").Value).IsEqualTo("7");
        await Assert.That(token.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Iat).Value).IsNotNull();
        await Assert.That(issued.ExpiresAt).IsEqualTo(now.AddDays(30));
        await Assert.That(issuer.ValidateRefresh(issued.Token)).IsEqualTo(new RefreshTokenPrincipal(user.Id, 7));
    }

    [Test]
    public async Task ValidateRefresh_RejectsAccessToken()
    {
        var options = Options.Create(new AuthenticationTokenOptions
        {
            SigningKey = "test-signing-key-with-at-least-32-bytes!"
        });
        var issuer = new JwtIssuer(options, TimeProvider.System);
        var access = issuer.Issue(
            new AuthenticatedUser(
                Guid.NewGuid(),
                "alice",
                UserRole.Administrator,
                UserKind.Human,
                7),
            DateTimeOffset.UtcNow);

        await Assert.That(issuer.ValidateRefresh(access.Token)).IsNull();
    }
}
