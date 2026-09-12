using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FluentStorage.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Teams.WriteUps;
using NoCTF.API.Security;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Storage;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.WriteUps;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class TeamWriteUpEndpointTests
{
    private static readonly Guid CompetitionId = Guid.CreateVersion7();
    private static readonly Guid TeamId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [Test]
    public async Task Observer_can_review_and_download_but_receives_no_judging_capability()
    {
        await using var app = await CreateApplicationAsync(canObserve: true, canJudge: false);
        using var client = app.GetTestClient();

        using var list = await client.GetAsync(
            $"/api/v1/competitions/{CompetitionId}/writeups");
        var review = await list.Content.ReadFromJsonAsync<TeamWriteUpReviewResponse>();
        using var download = await client.GetAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/{TeamId}/writeup/content");

        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(review!.CanJudge).IsFalse();
        await Assert.That(review.Items).Count().IsEqualTo(1);
        await Assert.That(download.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(download.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/pdf");
        await Assert.That(download.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(download.Headers.GetValues("Content-Security-Policy").Single())
            .IsEqualTo("sandbox; default-src 'none'");
        await Assert.That(download.Headers.GetValues("Cross-Origin-Resource-Policy").Single())
            .IsEqualTo("same-origin");
        await Assert.That(download.Headers.GetValues("X-Content-Type-Options").Single())
            .IsEqualTo("nosniff");
        await Assert.That(download.Content.Headers.ContentDisposition?.FileName)
            .Contains("dangername.pdf");
        var bytes = await download.Content.ReadAsByteArrayAsync();
        await Assert.That(bytes.Take(5).ToArray())
            .IsEquivalentTo("%PDF-"u8.ToArray());
    }

    [Test]
    public async Task Non_staff_cannot_list_or_download_team_WriteUps()
    {
        await using var app = await CreateApplicationAsync(canObserve: false, canJudge: false);
        using var client = app.GetTestClient();

        using var list = await client.GetAsync(
            $"/api/v1/competitions/{CompetitionId}/writeups");
        using var download = await client.GetAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/{TeamId}/writeup/content");

        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(download.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Team_member_upload_accepts_pdf_signature_and_rejects_disguised_content()
    {
        await using var app = await CreateApplicationAsync(canObserve: true, canJudge: true);
        using var client = app.GetTestClient();

        using var validContent = new MultipartFormDataContent();
        var validFile = new ByteArrayContent("%PDF-1.7\nvalid\n%%EOF"u8.ToArray());
        validFile.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        validContent.Add(validFile, "file", "team-writeup.pdf");
        using var accepted = await client.PutAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/me/writeup",
            validContent);

        using var invalidContent = new MultipartFormDataContent();
        var invalidFile = new ByteArrayContent("not a pdf"u8.ToArray());
        invalidFile.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        invalidContent.Add(invalidFile, "file", "team-writeup.pdf");
        using var rejected = await client.PutAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/me/writeup",
            invalidContent);

        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(rejected.StatusCode)
            .IsEqualTo(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task Team_member_upload_rejects_an_expired_submission_window()
    {
        await using var app = await CreateApplicationAsync(
            canObserve: true,
            canJudge: true,
            submissionContext: new TeamWriteUpSubmissionContext(
                TeamId,
                DateTimeOffset.UtcNow.AddHours(-1),
                0));
        using var client = app.GetTestClient();
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.7\nvalid\n%%EOF"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "team-writeup.pdf");

        using var response = await client.PutAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/me/writeup",
            content);
        var failure = await response.Content.ReadFromJsonAsync<TeamWriteUpFailureResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(failure!.Code)
            .IsEqualTo(TeamWriteUpFailureCode.WriteUpSubmissionDeadlinePassed);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        bool canObserve,
        bool canJudge,
        TeamWriteUpSubmissionContext? submissionContext = null)
    {
        var reference = new TeamWriteUpReference(
            TeamId,
            "Team",
            Guid.CreateVersion7(),
            "writeups/team.pdf",
            "../../danger\r\nname.pdf",
            TeamWriteUpRules.ContentType,
            16,
            new string('A', 64),
            ActorId,
            "Reviewer",
            DateTimeOffset.UtcNow);
        var store = Substitute.For<ITeamWriteUpStore>();
        store.ListAsync(CompetitionId, Arg.Any<CancellationToken>())
            .Returns([reference]);
        store.FindAsync(CompetitionId, TeamId, Arg.Any<CancellationToken>())
            .Returns(reference);
        store.FindSubmissionContextAsync(CompetitionId, ActorId, Arg.Any<CancellationToken>())
            .Returns(submissionContext ?? new TeamWriteUpSubmissionContext(
                TeamId,
                DateTimeOffset.UtcNow.AddDays(1),
                24));
        store.ReplaceAsync(
                CompetitionId,
                TeamId,
                ActorId,
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new TeamWriteUpSubmissionResult(
                TeamWriteUpSubmissionState.Updated,
                reference with
                {
                    FileId = call.ArgAt<Guid>(3),
                    SubmittedAt = call.ArgAt<DateTimeOffset>(4)
                }));
        var objects = Substitute.For<IStore>();
        objects.OpenRead(reference.ObjectKey, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(
                "%PDF-1.7\ntest\n%%EOF"u8.ToArray())));
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        var manager = new ManageTeamWriteUps(
            store,
            new ManagedFileUploads(registry, objects),
            objects,
            Substitute.For<ILeaderboardCache>());

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ListTeamWriteUpsEndpoint).Assembly];
            options.Filter = type => type == typeof(ListTeamWriteUpsEndpoint)
                || type == typeof(DownloadTeamWriteUpEndpoint)
                || type == typeof(UploadMyTeamWriteUpEndpoint);
        });
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(manager);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(
            new TestAuthorizer(canObserve, canJudge));
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestAuthorizer(bool canObserve, bool canJudge)
        : ICompetitionModerationAuthorizer
    {
        public Task<bool> CanModerateAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> CanJudgeAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(canJudge);

        public Task<bool> CanObserveAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(canObserve);
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
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
