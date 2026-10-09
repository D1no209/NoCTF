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
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Infrastructure.Authentication.Mfa;
using NoCTF.Domain.LiveSolo;
using FluentStorage.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Challenges.Configuration;

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
    public async Task Active_material_failure_returns_a_localizable_conflict_without_source_content()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var handled = await new RequestSafetyExceptionHandler(NullLogger<RequestSafetyExceptionHandler>.Instance).TryHandleAsync(context,
            new ChallengeMaterialMutationException(ChallengeMaterialMutationFailure.ActiveExecutionScope), CancellationToken.None);
        await Assert.That(handled).IsTrue(); await Assert.That(context.Response.StatusCode).IsEqualTo(409);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo("ActiveExecutionScope");
        await Assert.That(body.RootElement.GetProperty("messageKey").GetString()).IsEqualTo("api.challenges.material.activeExecutionScope");
        await Assert.That(body.RootElement.GetProperty("detail").GetString()).IsNotEmpty();
    }

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

    [Test]
    public async Task Runtime_commands_require_request_keys_and_return_a_scope_bound_status_route()
    {
        var runtimes = Substitute.For<ILiveSoloRuntimeStore>(); var id = Guid.NewGuid(); var competition = Guid.NewGuid();
        var match = Guid.NewGuid(); var round = Guid.NewGuid(); var question = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        runtimes.MutateAsync(Arg.Any<LiveSoloRuntimeCommand>(), Arg.Any<CancellationToken>()).Returns(new NoCTF.Application.Runtime.Instances.RuntimeMutationResult(
            new(id, competition, Guid.NewGuid(), null, Guid.NewGuid(), NoCTF.Domain.Runtime.RuntimePurpose.Player,
                NoCTF.Domain.Runtime.RuntimeKind.Container, NoCTF.Domain.Runtime.RuntimeProvider.Docker, NoCTF.Domain.Runtime.RuntimeState.Queued,
                null, now, null, null, null)));
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), Substitute.For<ILiveSoloAttachmentStore>(),
            Substitute.For<IStore>(), runtimes: runtimes);
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified-test");
        var path = $"/api/v1/competitions/{competition}/live-solo/matches/{match}/rounds/{round}/questions/{question}/runtime";
        using var missing = await client.PostAsJsonAsync(path, new { action = "Start" });
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await runtimes.DidNotReceive().MutateAsync(Arg.Any<LiveSoloRuntimeCommand>(), Arg.Any<CancellationToken>());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var accepted = await client.PostAsJsonAsync(path, new { action = "Start", matchId = Guid.NewGuid() });
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("runtimeInstanceId").GetGuid()).IsEqualTo(id);
        await Assert.That(body.RootElement.GetProperty("statusUrl").GetString()).IsEqualTo(path);
        await runtimes.Received(1).MutateAsync(Arg.Is<LiveSoloRuntimeCommand>(x => x != null && x.Scope.MatchId == match
            && x.Scope.RoundId == round && x.Scope.QuestionId == question && x.Scope.ActorId == Actor), Arg.Any<CancellationToken>());
        using var hidden = await client.GetAsync(path);
        await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(hidden.Headers.CacheControl!.NoStore).IsTrue();
    }
    [Test]
    public async Task Judge_action_requires_identity_reason_and_current_context_and_preserves_typed_failures()
    {
        var decisions = Substitute.For<ILiveSoloAdjudicationStore>();
        decisions.ApplyAsync(Arg.Any<AdjudicateLiveSoloMatch>(), Arg.Any<CancellationToken>())
            .Returns(new LiveSoloAdjudicationResult(null, null, null, LiveSoloFailure.Forbidden));
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), Substitute.For<ILiveSoloAttachmentStore>(), Substitute.For<IStore>(), decisions);
        using var client = app.GetTestClient();
        var competition = Guid.NewGuid(); var match = Guid.NewGuid(); var round = Guid.NewGuid();
        var path = $"/api/v1/competitions/{competition}/live-solo/matches/{match}/adjudications";
        var input = new { action = "Pause", reason = "裁判暂停", expectedMatchStamp = Guid.NewGuid(), expectedRoundId = round,
            expectedRoundStamp = Guid.NewGuid(), expectedTimelineRevision = 2, competitionId = Guid.NewGuid(), matchId = Guid.NewGuid() };
        using var anonymous = await client.PostAsJsonAsync(path, input);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified-test");
        using var invalid = await client.PostAsJsonAsync(path, new { action = "Pause", reason = " ", expectedMatchStamp = Guid.NewGuid() });
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        await decisions.DidNotReceive().ApplyAsync(Arg.Any<AdjudicateLiveSoloMatch>(), Arg.Any<CancellationToken>());
        using var forbidden = await client.PostAsJsonAsync(path, input);
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await decisions.Received(1).ApplyAsync(Arg.Is<AdjudicateLiveSoloMatch>(x => x != null && x.CompetitionId == competition
            && x.MatchId == match && x.ActorId == Actor && x.ExpectedRoundId == round), Arg.Any<CancellationToken>());
        decisions.ApplyAsync(Arg.Any<AdjudicateLiveSoloMatch>(), Arg.Any<CancellationToken>())
            .Returns(new LiveSoloAdjudicationResult(null, null, null, LiveSoloFailure.Conflict));
        using var stale = await client.PostAsJsonAsync(path, input);
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo("Conflict");
    }

    [Test]
    public async Task Decision_response_serializes_bounded_states_and_history_does_not_leak_to_a_participant()
    {
        var decisions = Substitute.For<ILiveSoloAdjudicationStore>(); var competition = Guid.NewGuid(); var match = Guid.NewGuid(); var round = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow; var stamp = Guid.NewGuid();
        decisions.ApplyAsync(Arg.Any<AdjudicateLiveSoloMatch>(), Arg.Any<CancellationToken>()).Returns(new LiveSoloAdjudicationResult(
            new(match, competition, LiveSoloMatchState.Paused, stamp, 2, 0, 0, Guid.NewGuid(), "left", Guid.NewGuid(), "right", round, null, []),
            new(round, match, 1, 0, LiveSoloRoundState.Running, stamp, 3, null, now, 900, 5000, true, null, null),
            new(Guid.NewGuid(), match, round, Actor, null, LiveSoloJudgeAction.Pause, "裁判暂停", now, LiveSoloMatchState.Running,
                LiveSoloMatchState.Paused, LiveSoloRoundState.Running, LiveSoloRoundState.Running, 0, 0, 0, 0, 2, 3)));
        decisions.ReadAsync(competition, match, Actor, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<LiveSoloAdjudicationView>?)null);
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), Substitute.For<ILiveSoloAttachmentStore>(), Substitute.For<IStore>(), decisions);
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified-test");
        var path = $"/api/v1/competitions/{competition}/live-solo/matches/{match}/adjudications";
        using var response = await client.PostAsJsonAsync(path, new { action = "Pause", reason = "裁判暂停", expectedMatchStamp = stamp,
            expectedRoundId = round, expectedRoundStamp = stamp, expectedTimelineRevision = 2 });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("decision").GetProperty("action").GetString()).IsEqualTo("Pause");
        await Assert.That(body.RootElement.GetProperty("decision").GetProperty("roundState").GetString()).IsEqualTo("Running");
        using var history = await client.GetAsync(path);
        await Assert.That(history.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(history.Headers.CacheControl!.NoStore).IsTrue();
    }

    [Test]
    public async Task Media_tokens_require_server_authentication_context_and_route_scope_and_cannot_request_a_public_viewer_role()
    {
        var media = Substitute.For<ILiveSoloMediaStore>(); var competition = Guid.NewGuid(); var match = Guid.NewGuid(); var generation = Guid.NewGuid();
        media.JoinAsync(Arg.Any<JoinLiveSoloMedia>(), Arg.Any<CancellationToken>()).Returns(new LiveSoloMediaResult(
            new(Guid.NewGuid(), match, generation, LiveSoloMediaState.Ready, false, [], 60, false), new("wss://media.invalid", "private-token", DateTimeOffset.UtcNow.AddMinutes(1))));
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), Substitute.For<ILiveSoloAttachmentStore>(), Substitute.For<IStore>(), media: media);
        using var client = app.GetTestClient();
        var path = $"/api/v1/competitions/{competition}/live-solo/matches/{match}/media/token";
        client.DefaultRequestHeaders.Authorization = new("Bearer", "verified-test");
        using var incomplete = await client.PostAsJsonAsync(path, new { generation, role = "Publisher", tokenVersion = 999,
            authentication = new { method = "Password", authenticatedAt = DateTimeOffset.UtcNow } });
        await Assert.That(incomplete.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await media.DidNotReceive().JoinAsync(Arg.Any<JoinLiveSoloMedia>(), Arg.Any<CancellationToken>());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "verified-media-test");
        using var publicRole = await client.PostAsJsonAsync(path, new { generation, role = "Viewer" });
        await Assert.That(publicRole.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var accepted = await client.PostAsJsonAsync(path, new { generation, role = "Publisher", competitionId = Guid.NewGuid(), matchId = Guid.NewGuid(), tokenVersion = 999 });
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(accepted.Headers.CacheControl!.NoStore).IsTrue();
        await media.Received(1).JoinAsync(Arg.Is<JoinLiveSoloMedia>(x => x != null && x.ActorId == Actor && x.CompetitionId == competition
            && x.MatchId == match && x.TokenVersion == 3 && x.Authentication!.Method == AuthenticationMethod.Password), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Public_program_contract_uses_only_delayed_fragment_frames_and_never_returns_raw_room_credentials()
    {
        var reader = Substitute.For<ILiveSoloProgramReader>(); var competition = Guid.NewGuid(); var match = Guid.NewGuid(); var segment = Guid.NewGuid();
        var state = new LiveSoloProgramStateView(DateTimeOffset.UtcNow.AddMinutes(-1), LiveSoloMatchState.Running, 2, 0, 1,
            Guid.NewGuid(), Guid.NewGuid(), "left", "right", Guid.NewGuid(), 1, LiveSoloRoundState.Running, 2, 10000, 900, false, []);
        reader.ReadAsync(competition, match, Actor, Arg.Any<CancellationToken>()).Returns(new LiveSoloProgramView(Guid.NewGuid(), 60, state,
            [new(segment, 8, TimeSpan.FromSeconds(2), state)], false));
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), Substitute.For<ILiveSoloAttachmentStore>(), Substitute.For<IStore>(), programs: reader);
        using var client = app.GetTestClient();
        var path = $"/api/v1/competitions/{competition}/live-solo/matches/{match}/program";
        using var metadata = await client.GetAsync(path);
        await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await metadata.Content.ReadAsStringAsync();
        await Assert.That(json).DoesNotContain("token"); await Assert.That(json).DoesNotContain("roomIdentity");
        using var body = JsonDocument.Parse(json);
        await Assert.That(body.RootElement.GetProperty("segments")[0].GetProperty("state").GetProperty("rightWins").GetInt32()).IsEqualTo(1);
        using var playlist = await client.GetAsync(path + "/playlist");
        await Assert.That(await playlist.Content.ReadAsStringAsync()).Contains("#EXT-X-MEDIA-SEQUENCE:8");
        await Assert.That(await playlist.Content.ReadAsStringAsync()).Contains("segments/" + segment);
        using var hidden = await client.GetAsync(path + "/segments/" + segment);
        await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
    [Test]
    public async Task Recording_contract_authenticates_decisions_and_streams_ranges_without_storage_metadata()
    {
        var store = Substitute.For<ILiveSoloRecordingStore>(); var competition = Guid.NewGuid(); var match = Guid.NewGuid(); var record = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        store.ListAsync(competition, match, Actor, true, 0, 20, Arg.Any<CancellationToken>()).Returns(new LiveSoloRecordingPage([
            new(record, Guid.NewGuid(), null, Actor, "player", null, null, LiveSoloRecordingState.Completed, stamp,
                DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow.AddDays(30), false, false, 4)], 1, true, true));
        store.OpenAsync(competition, match, record, Actor, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<LiveSoloRecordingContent?>(
            new(Guid.NewGuid(), new MemoryStream([1, 2, 3, 4]), "video/mp4", "recording.mp4")));
        store.ChangeAsync(Arg.Any<ChangeLiveSoloRecording>(), Arg.Any<CancellationToken>()).Returns(new LiveSoloRecordingChangeResult(null, LiveSoloFailure.Conflict));
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), Substitute.For<ILiveSoloAttachmentStore>(), Substitute.For<IStore>(), recordings: store);
        using var client = app.GetTestClient(); var path = $"/api/v1/competitions/{competition}/live-solo/matches/{match}/recordings";
        using var denied = await client.PostAsJsonAsync(path + $"/{record}/decisions", new { expectedStamp = stamp, action = "Hold", reason = "review" });
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "verified-test");
        using var list = await client.GetAsync(path + "?staff=true"); await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await list.Content.ReadAsStringAsync(); await Assert.That(body).DoesNotContain("objectKey"); await Assert.That(body).DoesNotContain("egressId");
        using var conflict = await client.PostAsJsonAsync(path + $"/{record}/decisions", new { competitionId = Guid.NewGuid(), matchId = Guid.NewGuid(),
            actorId = Guid.NewGuid(), expectedStamp = stamp, action = "Hold", reason = "review" });
        await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await store.Received(1).ChangeAsync(Arg.Is<ChangeLiveSoloRecording>(x => x != null && x.ActorId == Actor && x.CompetitionId == competition && x.MatchId == match), Arg.Any<CancellationToken>());
        using var request = new HttpRequestMessage(HttpMethod.Get, path + $"/{record}/file"); request.Headers.Range = new(1, 2);
        using var range = await client.SendAsync(request); await Assert.That(range.StatusCode).IsEqualTo(HttpStatusCode.PartialContent);
        await Assert.That((await range.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 2, 3 })).IsTrue();
        await Assert.That(range.Headers.CacheControl!.NoStore).IsTrue();
    }
    [Test]
    public async Task Participant_policy_is_authenticated_minimal_and_uses_only_the_principal_actor()
    {
        var policies = Substitute.For<ILiveSoloPlayerPolicyReader>(); var competition = Guid.NewGuid();
        policies.ReadAsync(competition, Actor, Arg.Any<CancellationToken>()).Returns(new LiveSoloPlayerPolicy(true, 2, 2, 60, false, false));
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), Substitute.For<ILiveSoloAttachmentStore>(), Substitute.For<IStore>(), policies: policies);
        using var client = app.GetTestClient(); var path = $"/api/v1/competitions/{competition}/live-solo/player-policy";
        using var anonymous = await client.GetAsync(path); await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "verified-test");
        using var allowed = await client.GetAsync(path + "?actorId=" + Guid.NewGuid()); await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.EnumerateObject().Count()).IsEqualTo(6);
        await Assert.That(body.RootElement.TryGetProperty("stageRules", out _)).IsFalse();
        await policies.Received(1).ReadAsync(competition, Actor, Arg.Any<CancellationToken>());
    }
    [Test]
    public async Task Settings_save_authenticates_and_maps_only_the_route_competition_and_principal_actor()
    {
        var competition = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var storage = Substitute.For<NoCTF.Application.Competitions.Configuration.ICompetitionConfigurationStore>();
        var policy = new LiveSoloCompetitionModeConfiguration { CompetitionId = competition, Enabled = true };
        var view = new NoCTF.Application.Competitions.Configuration.CompetitionConfigurationView(competition, NoCTF.Domain.Competitions.GameMode.LiveSolo,
            policy, NoCTF.Domain.Competitions.CompetitionStatus.Running, 2, [], now);
        storage.FindAsync(competition, Arg.Any<CancellationToken>()).Returns(view);
        storage.TryUpdateAsync(competition, Arg.Any<NoCTF.Domain.Competitions.CompetitionModeConfiguration>(), true, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new NoCTF.Application.Competitions.Configuration.CompetitionConfigurationUpdateResult(view));
        var validator = Substitute.For<NoCTF.Application.Competitions.Configuration.ICompetitionConfigurationValidator>();
        validator.Validate(Arg.Any<NoCTF.Domain.Competitions.GameMode>(), Arg.Any<NoCTF.Domain.Competitions.CompetitionModeConfiguration>(), 2,
            Arg.Any<IReadOnlyList<NoCTF.Domain.Challenges.CompetitionChallengeRules>>()).Returns(Array.Empty<string>());
        var authorizer = Substitute.For<NoCTF.Application.Teams.Moderation.ICompetitionModerationAuthorizer>();
        authorizer.CanModerateAsync(Actor, competition, Arg.Any<CancellationToken>()).Returns(true);
        var settings = new ManageLiveSoloConfiguration(new(storage), new(storage, validator), authorizer);
        await using var app = await HostAsync(Substitute.For<ILiveSoloMatchStore>(), Substitute.For<ILiveSoloAttachmentStore>(), Substitute.For<IStore>(), settings: settings);
        using var client = app.GetTestClient(); var path = $"/api/v1/competitions/{competition}/live-solo/configuration";
        var body = new { configuration = LiveSoloConfigurationMapping.ToContract(policy), actorId = Guid.NewGuid(), competitionId = Guid.NewGuid() };
        using var anonymous = await client.PutAsJsonAsync(path, body); await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "verified-test");
        using var accepted = await client.PutAsJsonAsync(path, body); await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await authorizer.Received(1).CanModerateAsync(Actor, competition, Arg.Any<CancellationToken>());
        await storage.Received(1).TryUpdateAsync(competition, Arg.Is<NoCTF.Domain.Competitions.CompetitionModeConfiguration>(x => x != null && x.CompetitionId == competition),
            true, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        authorizer.CanModerateAsync(Actor, competition, Arg.Any<CancellationToken>()).Returns(false);
        using var forbidden = await client.PutAsJsonAsync(path, body); await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }
    private static async Task<WebApplication> HostAsync(ILiveSoloMatchStore store, ILiveSoloAttachmentStore attachmentStore, IStore objects,
        ILiveSoloAdjudicationStore? decisions = null, ILiveSoloRuntimeStore? runtimes = null, ILiveSoloMediaStore? media = null, ILiveSoloProgramReader? programs = null,
        ILiveSoloRecordingStore? recordings = null, ILiveSoloPlayerPolicyReader? policies = null, ManageLiveSoloConfiguration? settings = null)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options => { options.DisableAutoDiscovery = true; options.Assemblies = [typeof(GetLiveSoloMatchEndpoint).Assembly];
            options.Filter = x => x == typeof(ListLiveSoloQuestionsEndpoint) || x == typeof(SubmitLiveSoloFlagEndpoint)
                || x == typeof(StartLiveSoloCountdownEndpoint) || x == typeof(DownloadLiveSoloAttachmentEndpoint)
                || x == typeof(AdjudicateLiveSoloMatchEndpoint) || x == typeof(ListLiveSoloAdjudicationsEndpoint)
                || x == typeof(GetLiveSoloRuntimeEndpoint) || x == typeof(MutateLiveSoloRuntimeEndpoint)
                || x == typeof(GetLiveSoloMediaEndpoint) || x == typeof(PrepareLiveSoloMediaEndpoint) || x == typeof(JoinLiveSoloMediaEndpoint)
                || x == typeof(GetLiveSoloProgramEndpoint) || x == typeof(GetLiveSoloProgramPlaylistEndpoint) || x == typeof(GetLiveSoloProgramSegmentEndpoint)
                || x == typeof(ListLiveSoloRecordingsEndpoint) || x == typeof(ChangeLiveSoloRecordingEndpoint) || x == typeof(GetLiveSoloRecordingEndpoint)
                || x == typeof(GetLiveSoloPlayerPolicyEndpoint) || settings is not null && x == typeof(SaveLiveSoloConfigurationEndpoint); });
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Bearer", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddSingleton(store); builder.Services.AddSingleton<ManageLiveSoloMatches>();
        builder.Services.AddSingleton(attachmentStore); builder.Services.AddSingleton(objects); builder.Services.AddSingleton<AccessLiveSoloAttachments>();
        builder.Services.AddSingleton(decisions ?? Substitute.For<ILiveSoloAdjudicationStore>()); builder.Services.AddSingleton<ManageLiveSoloAdjudication>();
        builder.Services.AddSingleton(runtimes ?? Substitute.For<ILiveSoloRuntimeStore>()); builder.Services.AddSingleton<ManageLiveSoloRuntimes>();
        builder.Services.AddSingleton(media ?? Substitute.For<ILiveSoloMediaStore>()); builder.Services.AddSingleton<ManageLiveSoloMedia>();
        builder.Services.AddSingleton(programs ?? Substitute.For<ILiveSoloProgramReader>());
        builder.Services.AddSingleton(recordings ?? Substitute.For<ILiveSoloRecordingStore>()); builder.Services.AddSingleton<ManageLiveSoloRecordings>();
        builder.Services.AddSingleton(policies ?? Substitute.For<ILiveSoloPlayerPolicyReader>());
        if (settings is not null) builder.Services.AddSingleton(settings);
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
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var bearer = Request.Headers.Authorization.ToString();
            if (bearer is not ("Bearer verified-test" or "Bearer verified-media-test")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()) };
            if (bearer == "Bearer verified-media-test")
            { claims.Add(new("token_version", "3")); claims.AddRange(AuthenticationContextClaims.Write(new(AuthenticationMethod.Password, DateTimeOffset.UtcNow))); }
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")), "Bearer")));
        }
    }
}
