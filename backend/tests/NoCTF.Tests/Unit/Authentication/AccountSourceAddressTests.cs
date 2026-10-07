using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Gameplay;
using System.Text.Json;

namespace NoCTF.Tests.Unit.Authentication;

public class AccountSourceAddressTests
{
    [Test]
    public async Task Failed_password_never_attributes_the_security_event_to_the_entered_account()
    {
        var store = NSubstitute.Substitute.For<NoCTF.Application.Authentication.Account.IUserAuthenticationStore>();
        var issuer = NSubstitute.Substitute.For<NoCTF.Application.Authentication.Account.IAccessTokenIssuer>();
        var recorder = NSubstitute.Substitute.For<IAccountActivityRecorder>();
        var id = Guid.NewGuid();
        NSubstitute.SubstituteExtensions.Returns(store.FindByLoginAsync("known-user", default),
            new NoCTF.Application.Authentication.Account.AuthenticatedUser(id, "known-user", UserRole.User, UserKind.Human, 0));
        var login = new NoCTF.Application.Authentication.Login.LoginUser(store, new NoCTF.Application.Authentication.Mfa.CompleteAuthentication(MfaTestSupport.Unrequired(), issuer, TimeProvider.System), TimeProvider.System, recorder);
        await login.ExecuteAsync(new("known-user", "wrong-password"));
        await NSubstitute.SubstituteExtensions.Received(recorder, 1).RecordLoginAsync(null, NSubstitute.Arg.Any<DateTimeOffset>(), default);
        await NSubstitute.SubstituteExtensions.DidNotReceive(recorder).RecordLoginAsync(id, NSubstitute.Arg.Any<DateTimeOffset>(), default);
    }
    [Test]
    [Arguments("192.0.2.20", null, "198.51.100.3", "192.0.2.20")]
    [Arguments("192.0.2.20", "192.0.2.1", "198.51.100.3", "192.0.2.20")]
    [Arguments("192.0.2.1", "192.0.2.1", "198.51.100.3", "198.51.100.3")]
    [Arguments("2001:db8::1", "2001:db8::1", "2001:db8:1::9", "2001:db8:1::9")]
    [Arguments("::ffff:192.0.2.20", null, "198.51.100.3", "192.0.2.20")]
    public async Task Actual_middleware_trusts_only_configured_peers(string peer, string? trusted, string forwarded, string expected)
    {
        var values = new Dictionary<string, string?>();
        if (trusted is not null) values["ForwardedHeaders:KnownProxies:0"] = trusted;
        await Assert.That(await ProcessAsync(values, peer, forwarded)).IsEqualTo(expected);
    }

    [Test]
    public async Task Multi_hop_chain_stops_at_first_untrusted_hop_and_honors_limit()
    {
        var values = new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.1",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/24", ["ForwardedHeaders:ForwardLimit"] = "2" };
        await Assert.That(await ProcessAsync(values, "192.0.2.1", "203.0.113.7, 10.0.0.2")).IsEqualTo("203.0.113.7");
        await Assert.That(await ProcessAsync(values, "192.0.2.1", "203.0.113.7, 198.51.100.8")).IsEqualTo("198.51.100.8");
        values["ForwardedHeaders:ForwardLimit"] = "1";
        await Assert.That(await ProcessAsync(values, "192.0.2.1", "203.0.113.7, 10.0.0.2")).IsEqualTo("10.0.0.2");
    }

    [Test]
    public async Task Raw_entity_serialization_and_event_logging_do_not_leak_private_fields()
    {
        var json = JsonSerializer.Serialize(new User { SchoolFullName = "private-name", SchoolStudentNumber = "001Ab" });
        await Assert.That(json).DoesNotContain("private-name");
        await Assert.That(json).DoesNotContain("001Ab");
        await Assert.That(JsonSerializer.Serialize(new FlagAttemptGameplayFact { SourceIpAddress = "192.0.2.9" })).DoesNotContain("192.0.2.9");
        await Assert.That(new AccountActivity(Guid.NewGuid(), AccountActivityKind.LoggedIn, DateTimeOffset.UtcNow, "192.0.2.9").ToString()).DoesNotContain("192.0.2.9");
    }

    private static async Task<string?> ProcessAsync(Dictionary<string, string?> values, string peer, string forwarded)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var method = typeof(RequestSourceAddress).Assembly.GetType("NoCTF.API.Composition.ForwardedProxyConfiguration")!
            .GetMethod("AddNoCtfForwardedHeaders", BindingFlags.Public | BindingFlags.Static)!;
        method.Invoke(null, [services, config]);
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = forwarded;
        context.Request.Headers["X-Forwarded-Proto"] = string.Join(", ", forwarded.Split(',').Select(_ => "https"));
        context.Request.Headers["X-Forwarded-Host"] = string.Join(", ", forwarded.Split(',').Select(_ => "example.test"));
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>());
        await middleware.Invoke(context);
        return new RequestSourceAddress(new HttpContextAccessor { HttpContext = context }).Address;
    }
}
