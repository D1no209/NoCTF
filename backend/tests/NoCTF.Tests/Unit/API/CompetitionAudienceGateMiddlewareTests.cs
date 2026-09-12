using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Access;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionAudienceGateMiddlewareTests
{
    [Test]
    public async Task Hidden_competition_returns_not_found_to_non_staff()
    {
        var harness = Harness.Create(CompetitionAccessMode.StaffOnly, canObserve: false);

        await harness.InvokeAsync();

        await Assert.That(harness.Context.Response.StatusCode)
            .IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(harness.NextCalled).IsFalse();
        await Assert.That(harness.Context.Response.Headers.CacheControl.ToString())
            .IsEqualTo("private,no-store");
    }

    [Test]
    public async Task Hidden_competition_allows_staff_and_marks_the_response_private()
    {
        var harness = Harness.Create(CompetitionAccessMode.StaffOnly, canObserve: true);

        await harness.InvokeAsync();

        await Assert.That(harness.NextCalled).IsTrue();
        await Assert.That(CompetitionAudienceGateMiddleware.IsStaffOnly(harness.Context))
            .IsTrue();
        await Assert.That(harness.Context.Response.Headers.Vary.ToString())
            .IsEqualTo("Authorization");
    }

    [Test]
    public async Task Public_and_internal_routes_are_not_restricted()
    {
        var publicHarness = Harness.Create(CompetitionAccessMode.Public, canObserve: false);
        await publicHarness.InvokeAsync();

        var internalHarness = Harness.Create(
            CompetitionAccessMode.StaffOnly,
            canObserve: false,
            path: "/api/internal/v1/competitions/test");
        await internalHarness.InvokeAsync();

        await Assert.That(publicHarness.NextCalled).IsTrue();
        await Assert.That(internalHarness.NextCalled).IsTrue();
        await internalHarness.Audiences.DidNotReceive().GetAccessModeAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Competition_http_surfaces_are_rejected_while_internal_routes_remain_available()
    {
        var competitionId = Guid.CreateVersion7();
        var audiences = Substitute.For<ICompetitionAudienceReader>();
        audiences.GetAccessModeAsync(competitionId, Arg.Any<CancellationToken>())
            .Returns(CompetitionAccessMode.StaffOnly);
        var user = Substitute.For<IUserContext>();
        var endpointCallCount = 0;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddSingleton(audiences);
        builder.Services.AddSingleton(Substitute.For<ICompetitionModerationAuthorizer>());
        builder.Services.AddSingleton(user);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<CompetitionAudienceGateMiddleware>();
        void Called() => endpointCallCount++;
        app.MapGet("/api/v1/competitions/{competitionId:guid}", () =>
        {
            Called();
            return TypedResults.Ok();
        });
        app.MapGet("/api/v1/competitions/{competitionId:guid}/poster", () =>
        {
            Called();
            return TypedResults.Ok();
        });
        app.MapGet("/api/v1/competitions/{competitionId:guid}/leaderboard", () =>
        {
            Called();
            return TypedResults.Ok();
        });
        app.MapPost(
            "/api/v1/competitions/{competitionId:guid}/challenges/{challengeId:guid}/flag-submissions",
            () =>
            {
                Called();
                return TypedResults.Ok();
            });
        app.MapPost("/api/internal/v1/competitions/{competitionId:guid}/probe", () =>
        {
            Called();
            return TypedResults.Ok();
        });
        await app.StartAsync();

        using var client = app.GetTestClient();
        var challengeId = Guid.CreateVersion7();
        (HttpMethod Method, string Path)[] blockedRequests =
        [
            (HttpMethod.Get, $"/api/v1/competitions/{competitionId}"),
            (HttpMethod.Get, $"/api/v1/competitions/{competitionId}/poster"),
            (HttpMethod.Get, $"/api/v1/competitions/{competitionId}/leaderboard"),
            (HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{challengeId}/flag-submissions")
        ];
        foreach (var blockedRequest in blockedRequests)
        {
            using var request = new HttpRequestMessage(
                blockedRequest.Method,
                blockedRequest.Path);
            using var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }

        using var internalResponse = await client.PostAsync(
            $"/api/internal/v1/competitions/{competitionId}/probe",
            content: null);
        await Assert.That(internalResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(endpointCallCount).IsEqualTo(1);
    }

    private sealed class Harness
    {
        private readonly CompetitionAudienceGateMiddleware middleware;
        private readonly ICompetitionModerationAuthorizer authorizer;
        private readonly IUserContext user;

        private Harness(
            DefaultHttpContext context,
            ICompetitionAudienceReader audiences,
            ICompetitionModerationAuthorizer authorizer,
            IUserContext user)
        {
            Context = context;
            Audiences = audiences;
            this.authorizer = authorizer;
            this.user = user;
            middleware = new(async _ =>
            {
                NextCalled = true;
                await Task.CompletedTask;
            });
        }

        public DefaultHttpContext Context { get; }
        public ICompetitionAudienceReader Audiences { get; }
        public bool NextCalled { get; private set; }

        public Task InvokeAsync() =>
            middleware.InvokeAsync(Context, Audiences, authorizer, user);

        public static Harness Create(
            CompetitionAccessMode accessMode,
            bool canObserve,
            string? path = null)
        {
            var competitionId = Guid.CreateVersion7();
            var userId = Guid.CreateVersion7();
            var context = new DefaultHttpContext();
            context.RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider();
            context.Request.Path = path ??
                $"/api/v1/competitions/{competitionId}/leaderboard";
            context.Request.RouteValues["competitionId"] = competitionId.ToString();
            var audiences = Substitute.For<ICompetitionAudienceReader>();
            audiences.GetAccessModeAsync(competitionId, Arg.Any<CancellationToken>())
                .Returns(accessMode);
            var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
            authorizer.CanObserveAsync(userId, competitionId, Arg.Any<CancellationToken>())
                .Returns(canObserve);
            var user = Substitute.For<IUserContext>();
            user.UserId.Returns(userId);
            return new(context, audiences, authorizer, user);
        }
    }
}
