using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.LiveSolo;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NSubstitute;
using NoCTF.Application.LiveSolo.Resources;
using FluentStorage.Storage;

namespace NoCTF.Tests.Unit.API;

public sealed class LiveSoloEndpointTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    [Test]
    public async Task Protected_routes_require_identity_and_never_return_an_unauthorized_question_body()
    {
        var store = Substitute.For<ILiveSoloMatchStore>();
        store.QuestionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LiveSoloQuestionView>?)null);
        await using var app = await HostAsync(store);
        using var client = app.GetTestClient();
        var path = $"/api/v1/competitions/{Guid.NewGuid()}/live-solo/matches/{Guid.NewGuid()}/rounds/{Guid.NewGuid()}/questions";
        using var anonymous = await client.GetAsync(path);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified-test");
        using var unauthorized = await client.GetAsync(path);
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(unauthorized.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(await unauthorized.Content.ReadAsStringAsync()).DoesNotContain("Description");
    }

    [Test]
    public async Task Submission_requires_the_existing_idempotency_gate_and_returns_the_server_order()
    {
        var store = Substitute.For<ILiveSoloMatchStore>();
        var fact = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        store.AdmitAsync(Arg.Any<LiveSoloAdmission>(), Arg.Any<CancellationToken>()).Returns(new LiveSoloAdmissionResult(fact, 42, now));
        await using var app = await HostAsync(store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified-test");
        var competition = Guid.NewGuid(); var match = Guid.NewGuid(); var round = Guid.NewGuid(); var question = Guid.NewGuid();
        var path = $"/api/v1/competitions/{competition}/live-solo/matches/{match}/rounds/{round}/questions/{question}/flag-submissions";
        using var missing = await client.PostAsJsonAsync(path, new { flag = "flag{test}" });
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await store.DidNotReceive().AdmitAsync(Arg.Any<LiveSoloAdmission>(), Arg.Any<CancellationToken>());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var accepted = await client.PostAsJsonAsync(path, new { flag = "flag{test}", competitionId = Guid.NewGuid(), matchId = Guid.NewGuid(), roundId = Guid.NewGuid(), questionId = Guid.NewGuid() });
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("admissionSequence").GetInt64()).IsEqualTo(42);
        await Assert.That(body.RootElement.GetProperty("gameplayFactId").GetGuid()).IsEqualTo(fact);
        await store.Received(1).AdmitAsync(Arg.Is<LiveSoloAdmission>(x => x != null && x.CompetitionId == competition && x.MatchId == match
            && x.RoundId == round && x.QuestionId == question && x.ActorId == Actor), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Unready_media_is_a_typed_conflict_and_does_not_claim_a_started_round()
    {
        var store = Substitute.For<ILiveSoloMatchStore>();
        store.StartCountdownAsync(Arg.Any<StartLiveSoloCountdown>(), Arg.Any<CancellationToken>())
            .Returns(new LiveSoloRoundResult(null, LiveSoloFailure.MediaUnavailable));
        await using var app = await HostAsync(store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified-test");
        var path = $"/api/v1/competitions/{Guid.NewGuid()}/live-solo/matches/{Guid.NewGuid()}/rounds/{Guid.NewGuid()}/countdown";
        using var unavailable = await client.PostAsJsonAsync(path, new { expectedStamp = Guid.NewGuid() });
        await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo("MediaUnavailable");
        await Assert.That(body.RootElement.TryGetProperty("startedAt", out _)).IsFalse();
    }

    private static async Task<WebApplication> HostAsync(ILiveSoloMatchStore store)
        => await HostAsync(store, Substitute.For<ILiveSoloAttachmentStore>(), Substitute.For<IStore>());

    [Test]
    public async Task Scoped_attachment_get_never_records_evidence_if_authorization_or_object_open_fails()
    {
        var store = Substitute.For<ILiveSoloAttachmentStore>(); var objects = Substitute.For<IStore>();
        var attachment = Guid.NewGuid();
        store.SelectAsync(Arg.Any<LiveSoloResourceRequest>(), attachment, Arg.Any<CancellationToken>()).Returns((LiveSoloAttachmentCandidate?)null);
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), store, objects);
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified-test");
        var path = $"/api/v1/competitions/{Guid.NewGuid()}/live-solo/matches/{Guid.NewGuid()}/rounds/{Guid.NewGuid()}/questions/{Guid.NewGuid()}/attachments/{attachment}";
        using var unauthorized = await client.GetAsync(path);
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await objects.DidNotReceive().OpenRead(Arg.Any<string>(), Arg.Any<CancellationToken>());
        store.SelectAsync(Arg.Any<LiveSoloResourceRequest>(), attachment, Arg.Any<CancellationToken>()).Returns(new LiveSoloAttachmentCandidate(
            new(attachment, "safe.bin", "application/octet-stream", 4), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "scoped/private-object"));
        objects.ObjectExists(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        objects.OpenRead(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<Stream>(new IOException("Failed object open")));
        using var unavailable = await client.GetAsync(path);
        await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await store.DidNotReceive().RecordDownloadAsync(Arg.Any<LiveSoloResourceRequest>(), Arg.Any<LiveSoloAttachmentCandidate>(), Arg.Any<CancellationToken>());
        await Assert.That(await unavailable.Content.ReadAsStringAsync()).DoesNotContain("private-object");
    }

    private static async Task<WebApplication> HostAsync(ILiveSoloMatchStore store, ILiveSoloAttachmentStore attachmentStore, IStore objects)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options => { options.DisableAutoDiscovery = true; options.Assemblies = [typeof(GetLiveSoloMatchEndpoint).Assembly];
            options.Filter = x => x == typeof(ListLiveSoloQuestionsEndpoint) || x == typeof(SubmitLiveSoloFlagEndpoint)
                || x == typeof(StartLiveSoloCountdownEndpoint) || x == typeof(DownloadLiveSoloAttachmentEndpoint); });
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Bearer", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddSingleton(store); builder.Services.AddSingleton<ManageLiveSoloMatches>();
        builder.Services.AddSingleton(attachmentStore); builder.Services.AddSingleton(objects); builder.Services.AddSingleton<AccessLiveSoloAttachments>();
        builder.Services.AddSingleton<IUserContext>(new TestUser()); builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IRequestAdmission>(new Admission());
        var source = Substitute.For<IRequestSourceAddress>(); source.Address.Returns("192.0.2.10");
        builder.Services.AddSingleton(source); builder.Services.AddSingleton(Substitute.For<IPatchUploadStore>());
        builder.Services.AddSingleton(new PostCommitDispatchStatus()); builder.Services.Configure<RequestAdmissionOptions>(_ => { });
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseMiddleware<RequestAdmissionMiddleware>();
        app.UseNoCtfEndpoints(); await app.StartAsync(); return app;
    }
    private sealed class Admission : IRequestAdmission
    {
        public ValueTask<IRequestAdmissionLease> AcquireAsync(IReadOnlyList<RateQuota> rates, IReadOnlyList<ConcurrentQuota> concurrency, CancellationToken ct) =>
            ValueTask.FromResult<IRequestAdmissionLease>(new Lease(ct));
    }
    private sealed class Lease(CancellationToken token) : IRequestAdmissionLease
    {
        public CancellationToken Token { get; } = token;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class TestUser : IUserContext { public Guid UserId => Actor; public bool IsAdministrator => false; }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization == "Bearer verified-test"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, Actor.ToString())], "Bearer")), "Bearer")) : AuthenticateResult.NoResult());
    }
}
