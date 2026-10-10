using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NoCTF.API.Security;

namespace NoCTF.Tests.Unit.API;

public sealed class LiveSoloViewerBrowserAccessTests
{
    [Test]
    public async Task Viewer_cookie_is_encrypted_prefix_bound_and_cannot_supply_authentication_to_other_routes()
    {
        var clock=new Clock();var environment=Substitute.For<IWebHostEnvironment>();environment.EnvironmentName.Returns("Production");
        var browser=new LiveSoloViewerBrowserAccess(new EphemeralDataProtectionProvider(),clock,environment);
        var competition=Guid.NewGuid();var match=Guid.NewGuid();var lease=Guid.NewGuid();
        var path=$"/api/v1/competitions/{competition}/live-solo/matches/{match}/program";
        var prepare=new DefaultHttpContext();prepare.Request.Path=path+"/viewer";prepare.Request.Headers.Authorization="Bearer PRIVATE_JWT";
        browser.Write(prepare,competition,match,lease);var cookie=prepare.Response.Headers.SetCookie.ToString();
        await Assert.That(cookie).DoesNotContain("PRIVATE_JWT");await Assert.That(cookie).DoesNotContain(lease.ToString());
        await Assert.That(cookie.ToLowerInvariant()).Contains("httponly");await Assert.That(cookie.ToLowerInvariant()).Contains("secure");
        await Assert.That(cookie.ToLowerInvariant()).Contains("samesite=strict");
        var get=new DefaultHttpContext();get.Request.Method="GET";get.Request.Path=path+"/playlist";get.Request.Headers.Cookie=cookie.Split(';')[0];
        get.Request.RouteValues["competitionId"]=competition.ToString();get.Request.RouteValues["matchId"]=match.ToString();
        await Assert.That(browser.LeaseId(get,competition,match)).IsEqualTo(lease);
        await Assert.That(browser.ReadAccessToken(get)).IsNull();
        get.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(new LiveSoloViewerBrowserAccessMetadata()),"programme"));
        await Assert.That(browser.ReadAccessToken(get)).IsEqualTo("PRIVATE_JWT");
        get.Request.Method="POST";await Assert.That(browser.ReadAccessToken(get)).IsNull();get.Request.Method="GET";
        get.Request.Headers.Authorization="Bearer NEW_JWT";await Assert.That(browser.ReadAccessToken(get)).IsNull();
        get.Request.Headers.Remove("Authorization");get.Request.Path=path+"-other";
        await Assert.That(browser.LeaseId(get,competition,match)).IsEqualTo(Guid.Empty);
        get.Request.Path=path;clock.Now=clock.Now.AddMinutes(30);
        await Assert.That(browser.LeaseId(get,competition,match)).IsEqualTo(Guid.Empty);
    }
    private sealed class Clock:TimeProvider {public DateTimeOffset Now{get;set;}=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
}
