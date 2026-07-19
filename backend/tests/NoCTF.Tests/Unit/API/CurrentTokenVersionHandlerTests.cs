using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Ports;

namespace NoCTF.Tests.Unit.API;

public class CurrentTokenVersionHandlerTests
{
    [Test]
    public async Task CurrentVersion_SucceedsRequirement()
    {
        var requirement = new CurrentTokenVersionRequirement();
        var userId = Guid.NewGuid();
        var context = Context(requirement, userId, 4);

        await new CurrentTokenVersionHandler(new Versions(userId, 4))
            .HandleAsync(context);

        await Assert.That(context.HasSucceeded).IsTrue();
    }

    [Test]
    public async Task StaleVersion_DoesNotSucceedRequirement()
    {
        var requirement = new CurrentTokenVersionRequirement();
        var userId = Guid.NewGuid();
        var context = Context(requirement, userId, 3);

        await new CurrentTokenVersionHandler(new Versions(userId, 4))
            .HandleAsync(context);

        await Assert.That(context.HasSucceeded).IsFalse();
    }

    [Test]
    public async Task MissingVersionClaim_DoesNotSucceedRequirement()
    {
        var requirement = new CurrentTokenVersionRequirement();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            "Bearer"));
        var context = new AuthorizationHandlerContext([requirement], principal, new DefaultHttpContext());

        await new CurrentTokenVersionHandler(new Versions(Guid.Empty, 0))
            .HandleAsync(context);

        await Assert.That(context.HasSucceeded).IsFalse();
    }

    private static AuthorizationHandlerContext Context(
        CurrentTokenVersionRequirement requirement,
        Guid userId,
        int version)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("token_version", version.ToString())
        ], "Bearer"));
        return new AuthorizationHandlerContext([requirement], principal, new DefaultHttpContext());
    }

    private sealed class Versions(Guid userId, int currentVersion) : IAccessTokenVersionReader
    {
        public Task<bool> IsCurrentAsync(Guid requestedUserId, int tokenVersion, CancellationToken cancellationToken) =>
            Task.FromResult(requestedUserId == userId && tokenVersion == currentVersion);
    }
}
