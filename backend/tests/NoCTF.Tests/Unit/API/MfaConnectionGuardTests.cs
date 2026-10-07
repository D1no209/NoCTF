using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.Tests.Unit.API;

public sealed class MfaConnectionGuardTests
{
    [Test]
    public async Task A_revoked_connection_is_aborted_and_removed_before_private_push()
    {
        var validator = Substitute.For<IMfaConnectionContextValidator>();
        validator.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<string, MfaFailure?>)call.Arg<IReadOnlyList<MfaContextValidationRequest>>()!
                .ToDictionary(value => value.Key, _ => (MfaFailure?)MfaFailure.LocalMfaRequired));
        var context = Context(); var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        guard.Register(context, MfaHubKind.Notifications);
        await Assert.That(await guard.EligibleAsync(MfaHubKind.Notifications, null, CancellationToken.None)).IsEmpty();
        context.Received(1).Abort(); await Assert.That(guard.IsRegistered(context.ConnectionId)).IsFalse();
    }

    [Test]
    public async Task A_failed_validation_dependency_cannot_deliver_private_messages()
    {
        var validator = Substitute.For<IMfaConnectionContextValidator>();
        validator.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IReadOnlyDictionary<string, MfaFailure?>>(new InvalidOperationException("Database unavailable")));
        var context = Context(); var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        guard.Register(context, MfaHubKind.Notifications);
        await Assert.That(await guard.EligibleAsync(MfaHubKind.Notifications, null, CancellationToken.None)).IsEmpty();
        context.Received(1).Abort();
    }

    [Test]
    public async Task A_revoked_method_does_not_call_the_hub_even_if_abort_notification_is_delayed()
    {
        var validator = Substitute.For<IMfaConnectionContextValidator>();
        validator.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, MfaFailure?>());
        var caller = Context(); var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        guard.Register(caller, MfaHubKind.Notifications);
        using var services = new ServiceCollection().BuildServiceProvider(); var invoked = false;
        var invocation = new HubInvocationContext(caller, services, new TestHub(), typeof(TestHub).GetMethod(nameof(TestHub.Ping))!, []);
        try
        {
            await new MfaHubFilter(guard).InvokeMethodAsync(invocation, _ => { invoked = true; return ValueTask.FromResult<object?>(null); });
        }
        catch (HubException) { }
        await Assert.That(invoked).IsFalse();
    }
    private static HubCallerContext Context()
    {
        var context = Substitute.For<HubCallerContext>(); context.ConnectionId.Returns("connection");
        context.UserIdentifier.Returns(Guid.NewGuid().ToString()); context.ConnectionAborted.Returns(CancellationToken.None);
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new("token_version", "0"),
            new("exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString())], "Bearer")));
        return context;
    }
    private sealed class TestHub : Hub { public void Ping() { } }
}
