using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.GameplayFacts;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class HistoricalAdjudicationPreviewHttpTests
{
    private const string BearerScheme = "Bearer";

    [Test]
    public async Task Event_pages_are_read_only_authorized_and_cursor_bound_to_the_fact()
    {
        var competitionId = Guid.NewGuid();
        var factId = Guid.NewGuid();
        var actor = new MutableUserContext(Guid.NewGuid());
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanReadHistoricalAuditAsync(Arg.Any<Guid>(), competitionId, Arg.Any<CancellationToken>()).Returns(true);
        var events = Substitute.For<IHistoricalAdjudicationEventStore>();
        var at = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid();
        events.ReadEventsAsync(competitionId, factId, Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new HistoricalAdjudicationEventPage([new(eventId, at, NoCTF.Domain.Competitions.Events.CompetitionEventKind.GameplayFactAdjudicated,
                GameplayFactState.Completed, GameplayFactResult.Correct)], at, eventId));
        await using var app = await CreateApplicationAsync(authorizer, user: actor, eventStore: events);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BearerScheme, actor.UserId.ToString());
        var route = $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/{factId}/adjudication-events";
        var first = await client.GetFromJsonAsync<HistoricalAdjudicationEventsResponse>(route + "?limit=1");
        await Assert.That(first!.Events.Single().EventId).IsEqualTo(eventId);
        await Assert.That(first.NextCursor).IsNotNull();
        var cursor = Uri.EscapeDataString(first.NextCursor!);
        using var crossFact = await client.GetAsync(route.Replace(factId.ToString(), Guid.NewGuid().ToString()) + "?cursor=" + cursor);
        await Assert.That(crossFact.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        actor.UserId = Guid.NewGuid();
        using var crossUser = await client.GetAsync(route + "?cursor=" + cursor);
        await Assert.That(crossUser.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        authorizer.CanReadHistoricalAuditAsync(Arg.Any<Guid>(), competitionId, Arg.Any<CancellationToken>()).Returns(false);
        using var forbidden = await client.GetAsync(route);
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(events.ReceivedCalls().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Observer_can_read_preview_but_participant_is_forbidden()
    {
        var competitionId = Guid.NewGuid();
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanReadHistoricalAuditAsync(Arg.Any<Guid>(), competitionId, Arg.Any<CancellationToken>())
            .Returns(true, false);
        await using var app = await CreateApplicationAsync(authorizer);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, Guid.NewGuid().ToString());
        var route = $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences";

        using var observer = await client.GetAsync(route);
        using var participant = await client.GetAsync(route);

        await Assert.That(observer.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var payload = await observer.Content
            .ReadFromJsonAsync<HistoricalAdjudicationDifferencePageResponse>();
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Items).IsEmpty();
        await Assert.That(participant.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Authorized_missing_competition_returns_not_found()
    {
        var competitionId = Guid.NewGuid();
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanReadHistoricalAuditAsync(Arg.Any<Guid>(), competitionId, Arg.Any<CancellationToken>())
            .Returns(true);
        var store = Substitute.For<IHistoricalAdjudicationEvidenceStore>();
        store.ReadAsync(
                competitionId,
                Arg.Any<Guid?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new HistoricalAdjudicationEvidencePage(
                HistoricalAdjudicationPreviewReadState.CompetitionNotFound,
                []));
        await using var app = await CreateApplicationAsync(authorizer, store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, Guid.NewGuid().ToString());

        using var response = await client.GetAsync(
            $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Cursor_is_bound_to_competition_user_and_challenge_filter_and_rejects_tampering()
    {
        var competitionId = Guid.NewGuid();
        var otherCompetitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var otherChallengeId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanReadHistoricalAuditAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var store = Substitute.For<IHistoricalAdjudicationEvidenceStore>();
        store.ReadAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new HistoricalAdjudicationEvidencePage(
                HistoricalAdjudicationPreviewReadState.Available,
                [
                    DifferenceEvidence(DateTimeOffset.UtcNow),
                    DifferenceEvidence(DateTimeOffset.UtcNow.AddMinutes(-1))
                ]));
        var user = new MutableUserContext(firstUserId);
        await using var app = await CreateApplicationAsync(authorizer, store, user);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, firstUserId.ToString());
        var route = $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences"
            + $"?competitionChallengeId={challengeId}&limit=1";
        using var first = await client.GetAsync(route);
        var rawPage = await first.Content.ReadAsStringAsync();
        var page = await first.Content
            .ReadFromJsonAsync<HistoricalAdjudicationDifferencePageResponse>();
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(rawPage).DoesNotContain("\"value\"");
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.NextCursor).IsNotNull();
        var cursor = Uri.EscapeDataString(page.NextCursor!);

        using var otherCompetition = await client.GetAsync(
            $"/api/v1/admin/competitions/{otherCompetitionId}/gameplay-facts/adjudication-differences"
            + $"?competitionChallengeId={challengeId}&limit=1&cursor={cursor}");
        using var otherFilter = await client.GetAsync(
            $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences"
            + $"?competitionChallengeId={otherChallengeId}&limit=1&cursor={cursor}");

        user.UserId = secondUserId;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, secondUserId.ToString());
        using var otherUser = await client.GetAsync(
            $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences"
            + $"?competitionChallengeId={challengeId}&limit=1&cursor={cursor}");

        user.UserId = firstUserId;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, firstUserId.ToString());
        var tampered = Uri.EscapeDataString(page.NextCursor![..^1]
            + (page.NextCursor[^1] == 'A' ? "B" : "A"));
        using var tamperedCursor = await client.GetAsync(
            $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences"
            + $"?competitionChallengeId={challengeId}&limit=1&cursor={tampered}");

        await Assert.That(otherCompetition.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(otherFilter.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(otherUser.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(tamperedCursor.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Legacy_awdp_duplicate_is_mapped_to_a_deterministic_correct_preview()
    {
        var competitionId = Guid.NewGuid();
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanReadHistoricalAuditAsync(Arg.Any<Guid>(), competitionId, Arg.Any<CancellationToken>())
            .Returns(true);
        var store = Substitute.For<IHistoricalAdjudicationEvidenceStore>();
        store.ReadAsync(
                competitionId,
                Arg.Any<Guid?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new HistoricalAdjudicationEvidencePage(
                HistoricalAdjudicationPreviewReadState.Available,
                [LegacyAwdpDuplicateEvidence(DateTimeOffset.UtcNow)]));
        await using var app = await CreateApplicationAsync(authorizer, store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, Guid.NewGuid().ToString());

        using var response = await client.GetAsync(
            $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences");
        var payload = await response.Content
            .ReadFromJsonAsync<HistoricalAdjudicationDifferencePageResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var item = payload!.Items.Single();
        await Assert.That(item.DeterministicExpectedResult)
            .IsEqualTo(GameplayFactResultProtocol.Correct);
        await Assert.That(item.Differences.Single().Kind)
            .IsEqualTo(AdjudicationDifferenceKindProtocol.CurrentDuplicateShouldBeCorrect);
        await Assert.That(item.Differences.Single().Certainty)
            .IsEqualTo(AdjudicationDifferenceCertaintyProtocol.Deterministic);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        ICompetitionModerationAuthorizer authorizer,
        IHistoricalAdjudicationEvidenceStore? store = null,
        IUserContext? user = null,
        IHistoricalAdjudicationEventStore? eventStore = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddNoCtfApi(builder.Configuration, includeInfrastructure: false);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(PreviewHistoricalAdjudicationDifferencesEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(PreviewHistoricalAdjudicationDifferencesEndpoint)
                || type == typeof(PreviewHistoricalAdjudicationDifferencesValidator)
                || type == typeof(GetHistoricalAdjudicationEventsEndpoint)
                || type == typeof(HistoricalAdjudicationEventsValidator);
        });
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = BearerScheme;
                options.DefaultChallengeScheme = BearerScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(BearerScheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(authorizer);
        builder.Services.AddSingleton(eventStore ?? Substitute.For<IHistoricalAdjudicationEventStore>());
        builder.Services.AddSingleton<ReadHistoricalAdjudicationEvents>();
        if (store is null)
        {
            store = Substitute.For<IHistoricalAdjudicationEvidenceStore>();
            store.ReadAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid?>(),
                    Arg.Any<DateTimeOffset?>(),
                    Arg.Any<Guid?>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns(new HistoricalAdjudicationEvidencePage(
                    HistoricalAdjudicationPreviewReadState.Available,
                    []));
        }
        store.ReadRestrictedAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => store.ReadAsync(call.ArgAt<Guid>(0), call.ArgAt<Guid?>(1), call.ArgAt<DateTimeOffset?>(2),
                call.ArgAt<Guid?>(3), call.ArgAt<int>(4), call.ArgAt<CancellationToken>(5)));
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<PreviewHistoricalAdjudicationDifferences>();
        user ??= new MutableUserContext(Guid.NewGuid());
        builder.Services.AddSingleton(user);
        var emailConfiguration = Substitute.For<IEmailVerificationConfigurationStore>();
        emailConfiguration.GetAsync(Arg.Any<CancellationToken>()).Returns(
            new EmailVerificationConfigurationView(
                false, "https://example.test", 30, 60, 30, 60, 3,
                string.Empty, 25, SmtpSecurityMode.StartTls, string.Empty,
                false, string.Empty, string.Empty, 10, DateTimeOffset.UtcNow));
        builder.Services.AddSingleton(emailConfiguration);
        builder.Services.AddSingleton(Substitute.For<IUserAuthenticationStore>());

        var app = builder.Build();
        app.UseNoCtfPipeline();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private static HistoricalAdjudicationEvidence DifferenceEvidence(
        DateTimeOffset occurredAt) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Challenge",
        Guid.NewGuid(),
        "Team",
        GameMode.Ctf,
        GameplayFactKind.FlagAttempt,
        GameplayFactResult.Correct,
        null,
        occurredAt,
        false,
        0,
        false,
        [new(Guid.NewGuid(), occurredAt, NoCTF.Domain.Competitions.Events.CompetitionEventKind.GameplayFactAdjudicated,
            GameplayFactState.Completed, GameplayFactResult.Correct)],
        []);

    private static HistoricalAdjudicationEvidence LegacyAwdpDuplicateEvidence(
        DateTimeOffset occurredAt) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "AWDP challenge",
        Guid.NewGuid(),
        "Team",
        GameMode.Awdp,
        GameplayFactKind.BreakAttempt,
        GameplayFactResult.Duplicate,
        GameplayFactFailureCode.DuplicateAchievement,
        occurredAt,
        false,
        0,
        false,
        [new(Guid.NewGuid(), occurredAt, NoCTF.Domain.Competitions.Events.CompetitionEventKind.GameplayFactAdjudicated,
            GameplayFactState.Completed, GameplayFactResult.Duplicate)],
        []);

    private sealed class MutableUserContext(Guid userId) : IUserContext
    {
        public Guid UserId { get; set; } = userId;
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
            var subject = Context.Request.Headers.Authorization
                .ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            if (!Guid.TryParse(subject, out var subjectId))
                return Task.FromResult(AuthenticateResult.Fail("Invalid test subject."));
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, subjectId.ToString())],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
