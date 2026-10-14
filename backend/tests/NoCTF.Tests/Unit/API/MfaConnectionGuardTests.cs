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

    [Test]
    public async Task Hub_connection_and_method_validate_only_the_current_connection()
    {
        var validator = AllowingValidator();
        var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        var unrelated = Context("unrelated");
        guard.Register(unrelated, MfaHubKind.Notifications);
        var caller = Context("caller");
        using var services = new ServiceCollection().BuildServiceProvider();
        var hub = new TestHub();
        var filter = new MfaHubFilter(guard);
        var connected = false;
        await filter.OnConnectedAsync(new HubLifetimeContext(caller, services, hub), _ => { connected = true; return Task.CompletedTask; });
        var invoked = false;
        var invocation = new HubInvocationContext(caller, services, hub, typeof(TestHub).GetMethod(nameof(TestHub.Ping))!, []);
        await filter.InvokeMethodAsync(invocation, _ => { invoked = true; return ValueTask.FromResult<object?>(null); });

        await Assert.That(connected && invoked).IsTrue();
        await validator.Received(2).ValidateAsync(
            Arg.Is<IReadOnlyList<MfaContextValidationRequest>>(requests => requests != null && requests.Count == 1 && requests[0].Key == "caller"), Arg.Any<CancellationToken>());
        unrelated.DidNotReceive().Abort();
        await guard.RevalidateAsync(CancellationToken.None);
        await validator.Received(1).ValidateAsync(
            Arg.Is<IReadOnlyList<MfaContextValidationRequest>>(requests => requests != null && requests.Count == 2), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task A_failed_targeted_validation_does_not_abort_unrelated_connections()
    {
        var validator = Substitute.For<IMfaConnectionContextValidator>();
        validator.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IReadOnlyDictionary<string, MfaFailure?>>(new InvalidOperationException("Database unavailable")));
        var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        var caller = Context("caller"); var unrelated = Context("unrelated");
        guard.Register(caller, MfaHubKind.Notifications); guard.Register(unrelated, MfaHubKind.Notifications);

        await guard.RevalidateAsync(caller.ConnectionId, CancellationToken.None);

        caller.Received(1).Abort(); unrelated.DidNotReceive().Abort();
        await Assert.That(guard.IsRegistered(unrelated.ConnectionId)).IsTrue();
    }

    [Test]
    public async Task A_connection_removed_during_validation_is_not_eligible()
    {
        var validator = Substitute.For<IMfaConnectionContextValidator>();
        var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        var caller = Context(); guard.Register(caller, MfaHubKind.Notifications);
        validator.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                guard.Remove(caller.ConnectionId);
                return (IReadOnlyDictionary<string, MfaFailure?>)new Dictionary<string, MfaFailure?> { [caller.ConnectionId] = null };
            });

        await Assert.That(await guard.EligibleConnectionsAsync(MfaHubKind.Notifications, [caller.ConnectionId], CancellationToken.None)).IsEmpty();
    }

    [Test]
    public async Task A_stale_validation_does_not_abort_a_replacement_registration()
    {
        var validator = Substitute.For<IMfaConnectionContextValidator>();
        var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        var caller = Context(); var replacement = Context(); guard.Register(caller, MfaHubKind.Notifications);
        validator.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                guard.Remove(caller.ConnectionId); guard.Register(replacement, MfaHubKind.Notifications);
                return (IReadOnlyDictionary<string, MfaFailure?>)new Dictionary<string, MfaFailure?> { [caller.ConnectionId] = MfaFailure.AccountUnavailable };
            });

        await Assert.That(await guard.EligibleConnectionsAsync(MfaHubKind.Notifications, [caller.ConnectionId], CancellationToken.None)).IsEmpty();
        await Assert.That(guard.IsRegistered(replacement.ConnectionId)).IsTrue();
        replacement.DidNotReceive().Abort();
    }

    [Test]
    public async Task Empty_or_wrong_hub_audiences_do_not_query_the_authentication_store()
    {
        var validator = AllowingValidator();
        var guard = new MfaConnectionGuard(validator, TimeProvider.System);
        var caller = Context(); guard.Register(caller, MfaHubKind.Notifications);

        await Assert.That(await guard.EligibleConnectionsAsync(MfaHubKind.Competition, [caller.ConnectionId, "missing"], CancellationToken.None)).IsEmpty();
        await guard.RevalidateAsync("missing", CancellationToken.None);

        await validator.DidNotReceive().ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>());
    }

    private static IMfaConnectionContextValidator AllowingValidator()
    {
        var validator = Substitute.For<IMfaConnectionContextValidator>();
        validator.ValidateAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<string, MfaFailure?>)call.Arg<IReadOnlyList<MfaContextValidationRequest>>()!
                .ToDictionary(value => value.Key, _ => (MfaFailure?)null));
        return validator;
    }

    private static HubCallerContext Context(string connectionId = "connection")
    {
        var context = Substitute.For<HubCallerContext>(); context.ConnectionId.Returns(connectionId);
        context.UserIdentifier.Returns(Guid.NewGuid().ToString()); context.ConnectionAborted.Returns(CancellationToken.None);
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new("token_version", "0"),
            new("exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString())], "Bearer")));
        return context;
    }
    private sealed class TestHub : Hub { public void Ping() { } }
}
