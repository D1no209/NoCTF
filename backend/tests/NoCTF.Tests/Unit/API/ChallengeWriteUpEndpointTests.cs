using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FluentStorage.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Challenges.WriteUps;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges.WriteUps;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class ChallengeWriteUpEndpointTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    [Test]
    public async Task Browser_grant_is_path_scoped_expiring_and_reusable_for_pdf_ranges()
    {
        var clock = new FakeTimeProvider();
        var environment = Substitute.For<IWebHostEnvironment>(); environment.EnvironmentName.Returns("Production");
        var browser = new WriteUpBrowserAccess(new EphemeralDataProtectionProvider(), clock, environment);
        var writer = new DefaultHttpContext(); writer.Request.Headers.Authorization = "Bearer verified-test";
        const string path = "/api/v1/competitions/one/writeups/versions/two/file";
        browser.Write(writer, path);
        var setCookie = writer.Response.Headers.SetCookie.ToString();
        await Assert.That(setCookie.ToLowerInvariant()).Contains("httponly");
        await Assert.That(setCookie.ToLowerInvariant()).Contains("secure");
        await Assert.That(setCookie.ToLowerInvariant()).Contains("samesite=strict");
        var reader = new DefaultHttpContext(); reader.Request.Method = "GET"; reader.Request.Path = path;
        reader.Request.Headers.Cookie = setCookie.Split(';')[0];
        reader.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new WriteUpBrowserAccessMetadata()), "PDF"));
        await Assert.That(browser.Read(reader)).IsEqualTo("verified-test");
        await Assert.That(browser.Read(reader)).IsEqualTo("verified-test");
        reader.Request.Path = "/api/v1/admin/users";
        await Assert.That(browser.Read(reader)).IsNull();
        reader.Request.Path = path; reader.Request.Method = "POST";
        await Assert.That(browser.Read(reader)).IsNull();
        reader.Request.Method = "GET"; clock.Advance(TimeSpan.FromMinutes(5));
        await Assert.That(browser.Read(reader)).IsNull();
    }

    [Test]
    public async Task Failed_pdf_open_does_not_record_an_unlock()
    {
        var store = Substitute.For<IChallengeWriteUpStore>(); var objects = Substitute.For<IStore>();
        var command = new UnlockWriteUp(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Actor, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var reference = new WriteUpFileReference(command.VersionId, "writeups/private.pdf", "solution.pdf", "application/pdf");
        store.ReadUnlockCandidateAsync(command, Arg.Any<CancellationToken>()).Returns(new WriteUpContentView(command.VersionId, WriteUpFormat.Pdf, null, reference));
        objects.ObjectExists(reference.ObjectKey, Arg.Any<CancellationToken>()).Returns(true);
        objects.OpenRead(reference.ObjectKey, Arg.Any<CancellationToken>()).Returns(Task.FromException<Stream>(new IOException("Unavailable")));
        var manager = new ManageChallengeWriteUps(store, new ManagedFileUploads(Substitute.For<IManagedFileUploadRegistry>(), objects), objects);
        await Assert.That(async () => await manager.UnlockAsync(command, CancellationToken.None)).Throws<IOException>();
        await store.DidNotReceive().UnlockAsync(Arg.Any<UnlockWriteUp>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Content_and_browser_authorization_do_not_reveal_unauthorized_documents()
    {
        var competition = Guid.NewGuid(); var challenge = Guid.NewGuid(); var version = Guid.NewGuid();
        var store = Substitute.For<IChallengeWriteUpStore>();
        store.ReadContentAsync(competition, challenge, version, Actor, false, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new WriteUpContentView(version, WriteUpFormat.Markdown, null, null, ChallengeWriteUpFailure.Forbidden));
        var objects = Substitute.For<IStore>();
        var manager = new ManageChallengeWriteUps(store, new ManagedFileUploads(Substitute.For<IManagedFileUploadRegistry>(), objects), objects);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options => { options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetChallengeWriteUpContentEndpoint).Assembly]; options.Filter = type =>
                type == typeof(GetChallengeWriteUpContentEndpoint) || type == typeof(PrepareChallengeWriteUpBrowserAccessEndpoint); });
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Bearer", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddSingleton(manager); builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()); builder.Services.AddSingleton<WriteUpBrowserAccess>();
        builder.Services.AddSingleton<IUserContext>(new TestUser());
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints(); await app.StartAsync();
        using var client = app.GetTestClient();
        var path = $"/api/v1/competitions/{competition}/challenges/{challenge}/writeups/versions/{version}";
        using var anonymous = await client.GetAsync(path);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified-test");
        using var denied = await client.GetAsync(path);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var grant = await client.PostAsync(path + "/browser-access", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        await Assert.That(grant.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(grant.Headers.Contains("Set-Cookie")).IsFalse();
        await objects.DidNotReceive().OpenRead(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
    private sealed class TestUser : IUserContext { public Guid UserId => Actor; public bool IsAdministrator => false; }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.Authorization == "Bearer verified-test" ? AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Actor.ToString())], "Bearer")), "Bearer")) : AuthenticateResult.NoResult());
    }
}
