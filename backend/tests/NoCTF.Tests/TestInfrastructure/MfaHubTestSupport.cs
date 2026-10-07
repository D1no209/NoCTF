using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.Tests;

internal static class MfaHubTestSupport
{
    internal static MfaConnectionGuard Guard(params (string ConnectionId, Guid UserId, MfaHubKind Kind)[] connections)
    {
        var store = Substitute.For<IMfaAuthenticationStore>();
        store.ValidateContextsAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<string, MfaFailure?>)call.Arg<IReadOnlyList<MfaContextValidationRequest>>()!.ToDictionary(value => value.Key, _ => (MfaFailure?)null));
        var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        var guard = new MfaConnectionGuard(new NoCTF.API.Composition.ScopedMfaConnectionContextValidator(services.GetRequiredService<IServiceScopeFactory>()), TimeProvider.System);
        foreach (var connection in connections)
        {
            var context = Substitute.For<HubCallerContext>();
            context.ConnectionId.Returns(connection.ConnectionId); context.UserIdentifier.Returns(connection.UserId.ToString());
            context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim("token_version", "0"), new Claim("exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString())], "Bearer")));
            guard.Register(context, connection.Kind);
        }
        return guard;
    }
}
