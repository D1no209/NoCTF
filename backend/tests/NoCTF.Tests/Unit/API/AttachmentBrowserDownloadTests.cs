using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;

namespace NoCTF.Tests.Unit.API;

public sealed class AttachmentBrowserDownloadTests
{
    [Test]
    public async Task Handoff_is_encrypted_http_only_path_scoped_and_accepted_only_on_marked_get()
    {
        var clock = new Clock(DateTimeOffset.UtcNow); var environment = Substitute.For<IWebHostEnvironment>(); environment.EnvironmentName.Returns("Production");
        var download = new AttachmentBrowserDownload(new EphemeralDataProtectionProvider(), clock, environment);
        const string path = "/api/v1/competitions/one/challenges/two/attachments/three";
        var prepare = new DefaultHttpContext(); prepare.Request.Headers.Authorization = "Bearer SECRET_ACCESS_JWT";
        download.Write(prepare, path);
        var cookie = prepare.Response.Headers.SetCookie.ToString();
        await Assert.That(cookie.Contains("SECRET_ACCESS_JWT", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && cookie.Contains("path=" + path, StringComparison.Ordinal)).IsTrue();
        var get = new DefaultHttpContext(); get.Request.Method = "GET"; get.Request.Path = path; get.Request.Headers.Cookie = cookie.Split(';')[0];
        await Assert.That(download.Read(get)).IsNull();
        get.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new AttachmentBrowserDownloadMetadata()), "attachment"));
        await Assert.That(download.Read(get)).IsEqualTo("SECRET_ACCESS_JWT");
        get.Request.Path = "/api/v1/admin/platform/users"; await Assert.That(download.Read(get)).IsNull();
        get.Request.Path = path; get.Request.Headers.Authorization = "Bearer INVALID"; await Assert.That(download.Read(get)).IsNull();
        get.Request.Headers.Remove("Authorization"); get.Request.Method = "POST"; await Assert.That(download.Read(get)).IsNull();
        get.Request.Method = "GET"; clock.Now = clock.Now.AddMinutes(3); await Assert.That(download.Read(get)).IsNull();
    }
    [Test]
    public async Task Preparation_does_not_assign_random_variants_or_record_attachment_evidence()
    {
        var store = Substitute.For<IChallengeAttachmentStore>(); var competition = Guid.NewGuid(); var challenge = Guid.NewGuid(); var actor = Guid.NewGuid();
        store.ListPlayerAsync(competition, challenge, actor, Arg.Any<CancellationToken>()).Returns(new ChallengeAttachmentSet(AttachmentDeliveryPolicy.RandomOnePerTeam, []));
        var prepare = new PrepareChallengeAttachmentDownload(store);
        await Assert.That(await prepare.ExecuteAsync(competition, challenge, null, actor, CancellationToken.None)).IsTrue();
        await Assert.That(await prepare.ExecuteAsync(competition, challenge, Guid.NewGuid(), actor, CancellationToken.None)).IsFalse();
        await store.DidNotReceiveWithAnyArgs().RecordPlayerDownloadAsync(default, default, default, default, default, default);
        await store.DidNotReceiveWithAnyArgs().GetPlayerAsync(default, default, default, default, default!, default);
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public DateTimeOffset Now { get; set; } = now; public override DateTimeOffset GetUtcNow() => Now; }
}
