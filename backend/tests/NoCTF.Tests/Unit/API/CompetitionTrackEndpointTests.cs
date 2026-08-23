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
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Administration.Teams;
using NoCTF.API.Endpoints.Competitions.Tracks;
using NoCTF.API.Security;
using NoCTF.Application.Common;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionTrackEndpointTests
{
    private static readonly Guid CompetitionId = Guid.CreateVersion7();
    private static readonly Guid TeamId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [Test]
    public async Task Public_endpoint_never_requests_internal_tracks()
    {
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, Access.Participant);
        using var response = await app.GetTestClient().GetAsync(
            $"/api/v1/competitions/{CompetitionId}/tracks");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(store.LastIncludeInternal).IsFalse();
    }

    [Test]
    public async Task Observer_can_read_all_tracks_but_cannot_update_them()
    {
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, Access.Observer);
        using var client = app.GetTestClient();

        using var read = await client.GetAsync(
            $"/api/v1/admin/competitions/{CompetitionId}/tracks");
        using var write = await client.PutAsJsonAsync(
            $"/api/v1/admin/competitions/{CompetitionId}/tracks",
            UpdateBody());

        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(store.LastIncludeInternal).IsTrue();
        await Assert.That(write.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(store.UpdateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Judge_cannot_assign_a_team_track_by_calling_the_endpoint_directly()
    {
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, Access.Judge);
        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/v1/admin/competitions/{CompetitionId}/teams/{TeamId}/track",
            new { trackKey = "internal" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(store.AssignCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Moderator_can_assign_an_internal_track_once()
    {
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, Access.Moderator);
        using var response = await app.GetTestClient().PutAsJsonAsync(
            $"/api/v1/admin/competitions/{CompetitionId}/teams/{TeamId}/track",
            new { trackKey = "internal" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(store.AssignCalls).IsEqualTo(1);
        await Assert.That(store.LastAssignedTrack).IsEqualTo("internal");
    }

    private static object UpdateBody() => new
    {
        tracks = new[]
        {
            new
            {
                key = "default",
                name = "Default",
                isDefault = true,
                isPublicSelectable = true,
                isInternal = false,
                earnsScore = true,
                earnsBlood = true,
                affectsDynamicChallengeScore = true,
                visibleOnLeaderboard = true,
                affectsCompetitiveResults = true
            }
        }
    };

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingStore store,
        Access access)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ListCompetitionTracksEndpoint).Assembly];
            options.Filter = type => type == typeof(ListCompetitionTracksEndpoint)
                || type == typeof(GetCompetitionTracksEndpoint)
                || type == typeof(UpdateCompetitionTracksEndpoint)
                || type == typeof(AssignTeamTrackEndpoint);
        });
        builder.Services.SwaggerDocument();
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ICompetitionTrackStore>(store);
        builder.Services.AddScoped<GetCompetitionTracks>();
        builder.Services.AddScoped<UpdateCompetitionTracks>();
        builder.Services.AddScoped<AssignTeamTrack>();
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(new TestAuthorizer(access));
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingStore : ICompetitionTrackStore
    {
        public bool? LastIncludeInternal { get; private set; }
        public int UpdateCalls { get; private set; }
        public int AssignCalls { get; private set; }
        public string? LastAssignedTrack { get; private set; }

        public Task<CompetitionTracksView?> GetAsync(
            Guid competitionId,
            Guid? viewerUserId,
            bool includeInternal,
            CancellationToken cancellationToken)
        {
            LastIncludeInternal = includeInternal;
            return Task.FromResult<CompetitionTracksView?>(new(
                CompetitionId,
                GameMode.Ctf,
                CompetitionStatus.Published,
                false,
                [Track("default", false), Track("internal", true)]));
        }

        public Task<OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>> UpdateAsync(
            UpdateCompetitionTracksCommand command,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.FromResult(OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>.Success(
                new(CompetitionId, GameMode.Ctf, CompetitionStatus.Published, false,
                    command.Tracks.Select(track => Track(track.Key, track.IsInternal)).ToArray())));
        }

        public Task<OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
            AssignTeamTrackCommand command,
            CancellationToken cancellationToken)
        {
            AssignCalls++;
            LastAssignedTrack = command.TrackKey;
            return Task.FromResult(OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>.Success(
                new(CompetitionId, TeamId, command.TrackKey)));
        }

        private static CompetitionTrackView Track(string key, bool internalTrack) => new(
            key,
            internalTrack ? "Internal" : "Default",
            !internalTrack,
            !internalTrack,
            internalTrack,
            !internalTrack,
            !internalTrack,
            !internalTrack,
            !internalTrack,
            !internalTrack);
    }

    private sealed class TestAuthorizer(Access access) : ICompetitionModerationAuthorizer
    {
        public Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(access == Access.Moderator);

        public Task<bool> CanJudgeAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(access is Access.Judge or Access.Moderator);

        public Task<bool> CanObserveAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(access is Access.Observer or Access.Judge or Access.Moderator);
    }

    private enum Access
    {
        Participant,
        Observer,
        Judge,
        Moderator
    }

    private sealed class ActorUserContext : IUserContext
    {
        public Guid UserId => ActorId;
        public bool IsAdministrator => false;
    }

    private sealed class TestBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ActorId.ToString())],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(
                new ClaimsPrincipal(identity),
                Scheme.Name)));
        }
    }
}
