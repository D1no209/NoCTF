using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Application.Authentication.Account;
using System.IdentityModel.Tokens.Jwt;

namespace NoCTF.Tests.Unit.API;

public class CurrentAccessTokenValidatorTests
{
    [Test]
    public async Task CurrentVersion_IsAccepted()
    {
        var userId = Guid.NewGuid();

        var isCurrent = await new CurrentAccessTokenValidator(new Versions(userId, 4))
            .IsCurrentAsync(Principal(userId, 4), CancellationToken.None);

        await Assert.That(isCurrent).IsTrue();
    }

    [Test]
    public async Task StaleVersion_IsRejected()
    {
        var userId = Guid.NewGuid();

        var isCurrent = await new CurrentAccessTokenValidator(new Versions(userId, 4))
            .IsCurrentAsync(Principal(userId, 3), CancellationToken.None);

        await Assert.That(isCurrent).IsFalse();
    }

    [Test]
    public async Task MissingVersionClaim_IsRejected()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            "Bearer"));

        var isCurrent = await new CurrentAccessTokenValidator(new Versions(Guid.Empty, 0))
            .IsCurrentAsync(principal, CancellationToken.None);

        await Assert.That(isCurrent).IsFalse();
    }

    [Test]
    public async Task LegacyAdministratorIssuedToken_RequiresProvenanceAndRegisteredJwtId()
    {
        var userId = Guid.NewGuid();
        var administratorId = Guid.NewGuid();
        var jwtId = Guid.NewGuid();
        var versions = new RecordingVersions(userId, 4, jwtId);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("token_version", "4"),
            new Claim(LegacyAccessTokenClaims.Impersonation, "true"),
            new Claim(LegacyAccessTokenClaims.ImpersonatorId, administratorId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jwtId.ToString("N"))
        ], "Bearer"));

        var isCurrent = await new CurrentAccessTokenValidator(versions)
            .IsCurrentAsync(principal, CancellationToken.None);

        await Assert.That(isCurrent).IsTrue();
        await Assert.That(versions.ObservedToken)
            .IsEqualTo(new LegacyAdministratorIssuedAccessToken(jwtId, administratorId));
    }

    [Test]
    public async Task LegacyAdministratorIssuedToken_WithMissingAdministratorClaim_IsRejected()
    {
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("token_version", "4"),
            new Claim(LegacyAccessTokenClaims.Impersonation, "true"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        ], "Bearer"));

        var isCurrent = await new CurrentAccessTokenValidator(new Versions(userId, 4))
            .IsCurrentAsync(principal, CancellationToken.None);

        await Assert.That(isCurrent).IsFalse();
    }

    [Test]
    public async Task BearerTokenValidatedEvent_RejectsStaleVersion()
    {
        var userId = Guid.NewGuid();
        var signingKey = new string('a', 32);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:SigningKey"] = signingKey,
                ["RunnerScoring:SigningKey"] = signingKey
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAccessTokenVersionReader>(new Versions(userId, 4));
        services.AddNoCtfAuthentication(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(AuthenticationRegistration.AccessScheme);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        var context = new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme(
                AuthenticationRegistration.AccessScheme,
                AuthenticationRegistration.AccessScheme,
                typeof(JwtBearerHandler)),
            options)
        {
            Principal = Principal(userId, 3)
        };

        await options.Events.OnTokenValidated(context);

        await Assert.That(context.Result?.Failure).IsNotNull();
    }

    private static ClaimsPrincipal Principal(Guid userId, int version) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("token_version", version.ToString())
        ], "Bearer"));

    private sealed class Versions(Guid userId, int currentVersion) : IAccessTokenVersionReader
    {
        public Task<bool> IsCurrentAsync(
            Guid requestedUserId,
            int tokenVersion,
            CancellationToken cancellationToken,
            LegacyAdministratorIssuedAccessToken? legacyAdministratorIssuedToken = null) =>
            Task.FromResult(requestedUserId == userId && tokenVersion == currentVersion);
    }

    private sealed class RecordingVersions(
        Guid userId,
        int currentVersion,
        Guid expectedJwtId) : IAccessTokenVersionReader
    {
        public LegacyAdministratorIssuedAccessToken? ObservedToken { get; private set; }

        public Task<bool> IsCurrentAsync(
            Guid requestedUserId,
            int tokenVersion,
            CancellationToken cancellationToken,
            LegacyAdministratorIssuedAccessToken? legacyAdministratorIssuedToken = null)
        {
            ObservedToken = legacyAdministratorIssuedToken;
            return Task.FromResult(
                requestedUserId == userId
                && tokenVersion == currentVersion
                && legacyAdministratorIssuedToken?.JwtId == expectedJwtId);
        }
    }
}
