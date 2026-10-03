using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FastEndpoints.Swagger;
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
using NoCTF.Application.Competitions.Directions;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Notifications;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionDirectionEndpointTests
{
    [Test, Arguments(true), Arguments(false)]
    public async Task Observers_can_read_but_only_moderators_can_save(bool canModerate)
    {
        var store = Substitute.For<ICompetitionDirectionStore>();
        var id = Guid.NewGuid();
        store.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new[] { new CompetitionDirectionView(id, "Web", "globe") });
        store.SaveAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<CompetitionDirectionView>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CompetitionDirectionsResult([new(id, "Web", "globe")]));
        await using var app = await App(store, canModerate);
        using var client = app.GetTestClient();
        var path = $"/api/v1/admin/competitions/{Guid.NewGuid()}/directions";
        using var anonymous = await client.GetAsync(path);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Add("X-Test-User", "authenticated");
        using var list = await client.GetAsync(path);
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var save = await client.PutAsJsonAsync(path, new { items = new[] { new { id, name = "Web", icon = "globe" } } });
        await Assert.That(save.StatusCode).IsEqualTo(canModerate ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        await Assert.That(store.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ICompetitionDirectionStore.SaveAsync))).IsEqualTo(canModerate ? 1 : 0);
    }
    [Test]
    public async Task Invalid_icon_suffixes_and_duplicate_names_are_rejected_before_storage()
    {
        var store = Substitute.For<ICompetitionDirectionStore>();
        await using var app = await App(store, true);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "authenticated");
        var path = $"/api/v1/admin/competitions/{Guid.NewGuid()}/directions";
        using var badIcon = await client.PutAsJsonAsync(path, new { items = new[] { new { id = Guid.NewGuid(), name = "Web", icon = "../../not-an-icon" } } });
        await Assert.That(badIcon.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await badIcon.Content.ReadAsStringAsync()).Contains("InvalidIcon");
        using var duplicate = await client.PutAsJsonAsync(path, new { items = new[] { new { id = Guid.NewGuid(), name = "Web", icon = "globe" }, new { id = Guid.NewGuid(), name = " web ", icon = "binary" } } });
        await Assert.That(duplicate.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await duplicate.Content.ReadAsStringAsync()).Contains("InvalidCatalog");
        await Assert.That(store.ReceivedCalls().Count()).IsEqualTo(0);
    }
    private static async Task<WebApplication> App(ICompetitionDirectionStore store, bool canModerate)
    {
        var builder=WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery=true;
            options.Assemblies=[typeof(GetCompetitionDirectionsEndpoint).Assembly];
            options.Filter=type => type==typeof(GetCompetitionDirectionsEndpoint)
                || type==typeof(SaveCompetitionDirectionsEndpoint) || type==typeof(SaveCompetitionDirectionsValidator);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestBearer>("Bearer",_=>{});
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(Substitute.For<IUserContext>());
        var authorizer=Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanObserveAsync(Arg.Any<Guid>(),Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(true);
        authorizer.CanModerateAsync(Arg.Any<Guid>(),Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(canModerate);
        builder.Services.AddSingleton(authorizer);
        builder.Services.AddSingleton(store);
        builder.Services.AddScoped<ManageCompetitionDirections>();
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
