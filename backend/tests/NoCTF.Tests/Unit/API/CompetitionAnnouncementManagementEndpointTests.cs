using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FastEndpoints.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Notifications;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Notifications;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionAnnouncementManagementEndpointTests
{
    [Test, Arguments(true), Arguments(false)]
    public async Task Observers_can_list_but_only_judges_can_edit_and_withdraw(bool canJudge)
    {
        var store = Substitute.For<ICompetitionAnnouncementManagementStore>();
        store.ListAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ManagedCompetitionAnnouncementPage([], 0));
        store.ChangeAsync(Arg.Any<ChangeCompetitionAnnouncementCommand>(), Arg.Any<CancellationToken>()).Returns(
            new ChangeCompetitionAnnouncementResult(new(Guid.NewGuid(), "Title", "Body", CompetitionAnnouncementAudience.Participants,
                CompetitionAnnouncementState.Published, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        await using var app = await App(store, canJudge);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "authenticated");
        var path = $"/api/v1/admin/competitions/{Guid.NewGuid()}/announcements";
        using var list = await client.GetAsync(path+"?offset=10&limit=5&desc=true&includeWithdrawn=true");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await store.Received(1).ListAsync(Arg.Any<Guid>(), true, 10, 5, true, Arg.Any<CancellationToken>());
        using var edit = await client.PatchAsJsonAsync(path+"/"+Guid.NewGuid(), new { title="Edited", body="Content" });
        using var delete = await client.DeleteAsync(path+"/"+Guid.NewGuid());
        await Assert.That(edit.StatusCode).IsEqualTo(canJudge ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        await Assert.That(delete.StatusCode).IsEqualTo(canJudge ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);
        await Assert.That(store.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ICompetitionAnnouncementManagementStore.ChangeAsync)))
            .IsEqualTo(canJudge ? 2 : 0);
        if (canJudge)
            await store.Received(1).ChangeAsync(Arg.Is<ChangeCompetitionAnnouncementCommand>(command => command != null
                && command.Action == CompetitionAnnouncementChangeAction.Edit && command.Title == "Edited"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Anonymous_and_invalid_requests_do_not_reach_announcement_storage()
    {
        var store = Substitute.For<ICompetitionAnnouncementManagementStore>();
        await using var app = await App(store, true);
        using var client = app.GetTestClient();
        var path=$"/api/v1/admin/competitions/{Guid.NewGuid()}/announcements";
        using var anonymous = await client.GetAsync(path);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Add("X-Test-User", "authenticated");
        using var invalid = await client.PatchAsJsonAsync(path+"/"+Guid.NewGuid(), new { title="", body="" });
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var invalidPage = await client.GetAsync(path+"?limit=201");
        await Assert.That(invalidPage.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(store.ReceivedCalls().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Editing_a_withdrawn_announcement_returns_a_stable_conflict()
    {
        var store = Substitute.For<ICompetitionAnnouncementManagementStore>();
        store.ChangeAsync(Arg.Any<ChangeCompetitionAnnouncementCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ChangeCompetitionAnnouncementResult(null, CompetitionAnnouncementFailure.Withdrawn));
        await using var app=await App(store,true);
        using var client=app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "authenticated");
        using var response=await client.PatchAsJsonAsync($"/api/v1/admin/competitions/{Guid.NewGuid()}/announcements/{Guid.NewGuid()}",new {title="Title",body="Body"});
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var json=await response.Content.ReadAsStringAsync();
        await Assert.That(json).Contains("Withdrawn");
    }

    private static async Task<WebApplication> App(ICompetitionAnnouncementManagementStore store, bool canJudge)
    {
        var builder=WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery=true;
            options.Assemblies=[typeof(ListCompetitionAnnouncementsEndpoint).Assembly];
            options.Filter=type => type==typeof(ListCompetitionAnnouncementsEndpoint) || type==typeof(ListCompetitionAnnouncementsValidator)
                || type==typeof(UpdateCompetitionAnnouncementEndpoint) || type==typeof(UpdateCompetitionAnnouncementValidator)
                || type==typeof(DeleteCompetitionAnnouncementEndpoint) || type==typeof(DeleteCompetitionAnnouncementValidator);
        });
        builder.Services.OpenApiDocument();
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestBearer>("Bearer",_=>{});
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(Substitute.For<IUserContext>());
        var authorizer=Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanObserveAsync(Arg.Any<Guid>(),Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(true);
        authorizer.CanJudgeAsync(Arg.Any<Guid>(),Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(canJudge);
        builder.Services.AddSingleton(authorizer);
        builder.Services.AddSingleton(store);
        builder.Services.AddScoped<ManageCompetitionAnnouncements>();
        builder.Services.AddSingleton(TimeProvider.System);
        var app=builder.Build();
        app.UseAuthentication();app.UseAuthorization();app.UseNoCtfEndpoints();
        await app.StartAsync();return app;
    }
    private sealed class TestBearer(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(!Request.Headers.ContainsKey("X-Test-User")
            ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString())],Scheme.Name)),Scheme.Name)));
    }
}
