using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NoCTF.API.Security;

namespace NoCTF.Tests.Unit.API;

public sealed class LiveSoloRecordingBrowserAccessTests
{
    [Test]
    public async Task Preview_cookie_is_encrypted_file_bound_path_scoped_expiring_and_reusable_for_ranges()
    {
        var clock = new Clock(); var environment = Substitute.For<IWebHostEnvironment>(); environment.EnvironmentName.Returns("Production");
        var browser = new LiveSoloRecordingBrowserAccess(new EphemeralDataProtectionProvider(), clock, environment);
        const string path = "/api/v1/competitions/one/live-solo/matches/two/recordings/three/file";
        var fileId = Guid.NewGuid(); var prepare = new DefaultHttpContext(); prepare.Request.Headers.Authorization = "Bearer PRIVATE_JWT";
        var expires = browser.Write(prepare, path, fileId); var cookie = prepare.Response.Headers.SetCookie.ToString();
        await Assert.That(cookie).DoesNotContain("PRIVATE_JWT");
        await Assert.That(cookie.ToLowerInvariant()).Contains("httponly"); await Assert.That(cookie.ToLowerInvariant()).Contains("secure");
        await Assert.That(cookie.ToLowerInvariant()).Contains("samesite=strict");
        var get = new DefaultHttpContext(); get.Request.Method = "GET"; get.Request.Path = path; get.Request.Headers.Cookie = cookie.Split(';')[0];
        await Assert.That(browser.Read(get)).IsNull();
        get.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new LiveSoloRecordingBrowserAccessMetadata()), "recording"));
        await Assert.That(browser.Read(get)).IsEqualTo("PRIVATE_JWT");
        await Assert.That(LiveSoloRecordingBrowserAccess.MatchesFile(get, fileId)).IsTrue();
        await Assert.That(LiveSoloRecordingBrowserAccess.MatchesFile(get, Guid.NewGuid())).IsFalse();
        get.Request.Headers.Range = "bytes=100-"; await Assert.That(browser.Read(get)).IsEqualTo("PRIVATE_JWT");
        await Assert.That(get.Response.Headers.SetCookie.Count).IsEqualTo(0);
        get.Request.Path = "/api/v1/competitions/one/live-solo/matches/two/recordings/other/file"; await Assert.That(browser.Read(get)).IsNull();
        get.Request.Path = path; get.Request.Headers.Authorization = "Bearer INVALID"; await Assert.That(browser.Read(get)).IsNull();
        get.Request.Headers.Remove("Authorization"); clock.Now = expires; await Assert.That(browser.Read(get)).IsNull();
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
