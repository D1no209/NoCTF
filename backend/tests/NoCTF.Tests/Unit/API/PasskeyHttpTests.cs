using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.API.Endpoints.Authentication.Passkeys;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Authentication.Passkeys;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using System.Net;
using System.Net.Http.Json;

namespace NoCTF.Tests.Unit.API;

public sealed class PasskeyHttpTests
{
    [Test]
    public async Task Pending_passkey_login_sets_only_a_secure_browser_bound_Mfa_cookie_and_never_issues_tokens()
    {
        var id = Guid.NewGuid(); var credential = Guid.NewGuid(); var user = new AuthenticatedUser(id, "test", UserRole.User, UserKind.Human, 0);
        var store = Substitute.For<IPasskeyStore>(); var mfa = Substitute.For<IMfaAuthenticationStore>(); var issuer = Substitute.For<IAccessTokenIssuer>();
        store.FinishLoginAsync(Arg.Any<PasskeyBrowserCredential>(), Arg.Any<string>(), "https://noctf.test", Arg.Any<CancellationToken>())
            .Returns(OperationResult<PasskeyPrimaryAuthentication, PasskeyFailure>.Success(new(user, credential, "/")));
        mfa.ReadAccountAsync(id, Arg.Any<CancellationToken>()).Returns(new MfaAccountSnapshot(user, false, true, Guid.NewGuid(), Guid.NewGuid(), 10, false));
        mfa.CreateFlowAsync(Arg.Any<PrimaryAuthentication>(), MfaChallengePurpose.Login, Arg.Any<CancellationToken>()).Returns((
            new MfaFlowView(Guid.NewGuid(), MfaChallengePurpose.Login, DateTimeOffset.UtcNow.AddMinutes(5), 5, "test", true, "/"), new MfaBrowserCredential(Guid.NewGuid(), new string('x', 43))));
        await using var app = await App(store, mfa, issuer); using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Origin", "https://noctf.test");
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-NoCTF.Passkey={Guid.NewGuid():N}.{new string('x', 43)}");
        using var response = await client.PostAsJsonAsync("/api/v1/auth/passkeys/login/complete", new { credentialJson = "{}" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(); await Assert.That(body).Contains("MfaRequired"); await Assert.That(body).DoesNotContain("accessToken");
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        await Assert.That(cookies.Any(value => value.StartsWith("__Host-NoCTF.Mfa=", StringComparison.Ordinal) && value.Contains("secure") && value.Contains("httponly") && value.Contains("samesite=strict"))).IsTrue();
        issuer.DidNotReceive().Issue(Arg.Any<AuthenticatedUser>(), Arg.Any<AuthenticationContext>(), Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan?>());
        issuer.DidNotReceive().IssueRefresh(Arg.Any<AuthenticatedUser>(), Arg.Any<AuthenticationContext>());
    }

    [Test]
    public async Task Missing_browser_cookie_does_not_run_assertion_verification()
    {
        var store = Substitute.For<IPasskeyStore>(); await using var app = await App(store, Substitute.For<IMfaAuthenticationStore>(), Substitute.For<IAccessTokenIssuer>());
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("Origin", "https://noctf.test");
        using var response = await client.PostAsJsonAsync("/api/v1/auth/passkeys/login/complete", new { credentialJson = "{}" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
        await store.DidNotReceive().FinishLoginAsync(Arg.Any<PasskeyBrowserCredential>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static async Task<WebApplication> App(IPasskeyStore store, IMfaAuthenticationStore mfa, IAccessTokenIssuer issuer)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production }); builder.WebHost.UseTestServer();
        builder.Services.AddLogging(); builder.Services.AddProblemDetails(); builder.Services.AddAuthorization();
        builder.Services.AddFastEndpoints(options => { options.DisableAutoDiscovery = true; options.Assemblies = [typeof(CompletePasskeyLoginEndpoint).Assembly]; options.Filter = type => type == typeof(CompletePasskeyLoginEndpoint) || type == typeof(CompletePasskeyLoginValidator); });
        builder.Services.AddSingleton(store); builder.Services.AddSingleton(mfa); builder.Services.AddSingleton(issuer); builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<RefreshHttpOptions>(_ => { }); builder.Services.AddSingleton<MfaBrowserFlow>(); builder.Services.AddSingleton<PasskeyBrowserFlow>();
        builder.Services.AddScoped<CompleteAuthentication>(); builder.Services.AddScoped<AuthenticateWithPasskey>();
        var app = builder.Build(); app.UseNoCtfEndpoints(); await app.StartAsync(); return app;
    }
}
