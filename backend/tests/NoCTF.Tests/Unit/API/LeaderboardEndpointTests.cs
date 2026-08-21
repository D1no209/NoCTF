using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.API.Security;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class LeaderboardEndpointTests
{
    [Test]
    public async Task Scoreboard_identity_values_are_exact_decimal_strings()
    {
        var competitionId = Guid.CreateVersion7();
        const long catalogRevision = 9_007_199_254_740_993;
        const long schemaRevision = 9_007_199_254_740_995;
        const long version = 638_914_000_000_000_001;
        var projection = CreateProjection(competitionId, [], [], []) with
        {
            ChallengeCatalog = new(competitionId, catalogRevision, []),
            Schema = new(competitionId, GameMode.Ctf, schemaRevision, catalogRevision, [], []),
            Snapshot = new(competitionId, version, schemaRevision, DateTimeOffset.UtcNow, null, [], [])
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher());
        using var client = app.GetTestClient();

        using var catalogResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/challenges");
        using var schemaResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/schema");
        using var snapshotResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync());
        using var schema = JsonDocument.Parse(await schemaResponse.Content.ReadAsStringAsync());
        using var snapshot = JsonDocument.Parse(await snapshotResponse.Content.ReadAsStringAsync());

        await Assert.That(catalog.RootElement.GetProperty("revision").ValueKind)
            .IsEqualTo(JsonValueKind.String);
        await Assert.That(schema.RootElement.GetProperty("revision").ValueKind)
            .IsEqualTo(JsonValueKind.String);
        await Assert.That(schema.RootElement.GetProperty("challengeCatalogRevision").ValueKind)
            .IsEqualTo(JsonValueKind.String);
        await Assert.That(snapshot.RootElement.GetProperty("version").GetString())
            .IsEqualTo(version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(snapshot.RootElement.GetProperty("schemaRevision").ValueKind)
            .IsEqualTo(JsonValueKind.String);

        var realtime = ScoreboardUpdated.From(projection);
        await Assert.That(realtime.Version)
            .IsEqualTo(version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(realtime.SchemaRevision)
            .IsEqualTo(schemaRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(realtime.ChallengeCatalogRevision)
            .IsEqualTo(catalogRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task Cached_snapshot_is_returned_without_requesting_projection()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(),
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(messages.ProjectedCompetitionIds).Count()
            .IsEqualTo(0);
    }

    [Test]
    public async Task Missing_snapshot_returns_accepted_with_retry_after()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        var leaderboard = new CachedLeaderboard(missing: true);
        await using var app = await CreateApplicationAsync(
            competitionId,
            leaderboard,
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(response.Headers.RetryAfter?.Delta)
            .IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
        await Assert.That(leaderboard.InvalidatedCompetitionIds)
            .IsEquivalentTo([competitionId]);
    }

    [Test]
    public async Task Projection_failure_returns_service_unavailable_without_requeue()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(
                missing: true,
                lastFailureAt: DateTimeOffset.UtcNow),
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
    }

    [Test]
    public async Task Unknown_competition_returns_not_found()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(),
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{Guid.CreateVersion7()}/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
    }

    [Test]
    public async Task Blackout_returns_hidden_empty_projection_without_queueing_work()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(),
            messages,
            CompetitionLeaderboardVisibility.Blackout,
            LeaderboardDataScope.Hidden);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        var body = await response.Content.ReadFromJsonAsync<ScoreboardSnapshotResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Visibility)
            .IsEqualTo(LeaderboardVisibilityProtocol.Blackout);
        await Assert.That(body.DataScope).IsEqualTo(LeaderboardDataScopeProtocol.Hidden);
        await Assert.That(body.Teams).IsEmpty();
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
    }

    [Test]
    public async Task Frozen_projection_ignores_historical_round_requests_and_keeps_the_persisted_snapshot()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        var snapshots = Substitute.For<ILeaderboardSnapshotFactory>();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(frozen: true),
            messages,
            CompetitionLeaderboardVisibility.Frozen,
            LeaderboardDataScope.Frozen,
            gameMode: GameMode.Awdp,
            snapshotFactory: snapshots);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard?endingRound=1");
        var body = await response.Content.ReadFromJsonAsync<ScoreboardSnapshotResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Visibility)
            .IsEqualTo(LeaderboardVisibilityProtocol.Frozen);
        await Assert.That(body.DataScope).IsEqualTo(LeaderboardDataScopeProtocol.Frozen);
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();

        using var schemaResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/schema?endingRound=1");
        await Assert.That(schemaResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var slotResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{Guid.CreateVersion7()}/columns/0?endingRound=1");
        await Assert.That(slotResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        _ = snapshots.DidNotReceive().CreateScoreboardWindowAsync(
            Arg.Any<Guid>(),
            Arg.Any<int>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Catalog_and_schema_hide_unpublished_challenges_and_remap_columns()
    {
        var competitionId = Guid.CreateVersion7();
        var publishedId = Guid.CreateVersion7();
        var unpublishedId = Guid.CreateVersion7();
        var projection = CreateProjection(
            competitionId,
            [
                new(publishedId, "Published", "PWN", "PWN", 1, true, 2),
                new(unpublishedId, "Draft", "WEB", "WEB", 2, false, 3)
            ],
            [new(4, publishedId, null), new(9, unpublishedId, null)],
            []);
        var participantProjection = CreateProjection(
            competitionId,
            [new(publishedId, "Published", "PWN", "PWN", 1, true, 2)],
            [new(0, publishedId, null)],
            []);
        projection = projection with
        {
            ParticipantView = ScoreboardAudienceView.From(participantProjection)
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher());
        using var client = app.GetTestClient();

        using var catalogResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/challenges");
        using var schemaResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/schema");
        var catalog = await catalogResponse.Content
            .ReadFromJsonAsync<ScoreboardChallengeCatalogResponse>();
        var schema = await schemaResponse.Content
            .ReadFromJsonAsync<ScoreboardSchemaResponse>();

        await Assert.That(catalogResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(schemaResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(catalog!.Items.Select(item => item.Id))
            .IsEquivalentTo([publishedId]);
        await Assert.That(schema!.Columns).Count().IsEqualTo(1);
        await Assert.That(schema.Columns[0].Index).IsEqualTo(0);
        await Assert.That(schema.Columns[0].CompetitionChallengeId).IsEqualTo(publishedId);
        await Assert.That(schema.ChallengeCatalogRevision).IsEqualTo(catalog.Revision);
    }

    [Test]
    public async Task Public_slot_detail_remaps_allocations_after_hiding_unpublished_challenges()
    {
        var competitionId = Guid.CreateVersion7();
        var publishedId = Guid.CreateVersion7();
        var unpublishedId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var publishedEntryId = Guid.CreateVersion7();
        var unpublishedEntryId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow;
        var projection = CreateProjection(
            competitionId,
            [
                new(publishedId, "Published", "PWN", "PWN", 1, true, 2),
                new(unpublishedId, "Draft", "WEB", "WEB", 2, false, 3)
            ],
            [new(4, publishedId, null), new(9, unpublishedId, null)],
            [
                new(teamId, "Alpha", "default", 1, ScoreboardRankingState.Eligible, 30, 0, [],
                [
                    new(4, ScoreboardScoreState.Provisional, 10, 0, 10, 1, [], []),
                    new(9, ScoreboardScoreState.Provisional, 20, 0, 20, 1, [], [])
                ])
            ]);
        projection = projection with
        {
            EntryAllocations =
            [
                new(teamId, 4,
                    new(publishedEntryId, ScoreboardEntryKind.Solve, ScoreboardEntryOutcome.Succeeded,
                        null, null, occurredAt, null, 10, 0, 10)),
                new(teamId, 9,
                    new(unpublishedEntryId, ScoreboardEntryKind.Solve, ScoreboardEntryOutcome.Succeeded,
                        null, null, occurredAt, null, 20, 0, 20))
            ]
        };
        var participantProjection = CreateProjection(
            competitionId,
            [new(publishedId, "Published", "PWN", "PWN", 1, true, 2)],
            [new(0, publishedId, null)],
            [
                new(teamId, "Alpha", "default", 2, ScoreboardRankingState.Eligible, 10, 0, [],
                [new(0, ScoreboardScoreState.Provisional, 10, 0, 10, 1, [], [])])
            ]);
        participantProjection = participantProjection with
        {
            EntryAllocations =
            [
                new(teamId, 0,
                    new(publishedEntryId, ScoreboardEntryKind.Solve, ScoreboardEntryOutcome.Succeeded,
                        null, null, occurredAt, null, 10, 0, 10))
            ]
        };
        projection = projection with
        {
            ParticipantView = ScoreboardAudienceView.From(participantProjection)
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher());
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/columns/0");
        using var leaderboardResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        var detail = await response.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();
        var leaderboard = await leaderboardResponse.Content
            .ReadFromJsonAsync<ScoreboardSnapshotResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(leaderboardResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail!.ColumnIndex).IsEqualTo(0);
        await Assert.That(detail.Items.Select(item => item.Id)).IsEquivalentTo([publishedEntryId]);
        await Assert.That(detail.Items.Select(item => item.Id)).DoesNotContain(unpublishedEntryId);
        await Assert.That(leaderboard!.Teams).HasSingleItem();
        await Assert.That(leaderboard.Teams[0].TotalScore).IsEqualTo(10);
        await Assert.That(leaderboard.Teams[0].Rank).IsEqualTo(2);
    }

    [Test]
    public async Task Hidden_schema_preserves_the_real_game_mode_without_exposing_columns()
    {
        var competitionId = Guid.CreateVersion7();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(),
            new RecordingMessagePublisher(),
            CompetitionLeaderboardVisibility.Blackout,
            LeaderboardDataScope.Hidden,
            gameMode: GameMode.Awdp);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/schema");
        var schema = await response.Content.ReadFromJsonAsync<ScoreboardSchemaResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(schema!.Mode).IsEqualTo(GameModeProtocol.Awdp);
        await Assert.That(schema.Columns).IsEmpty();
        await Assert.That(schema.Rounds).IsEmpty();
    }

    [Test]
    public async Task Slot_detail_uses_signed_team_scoped_cursor_and_returns_authoritative_totals()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var firstTeamId = Guid.CreateVersion7();
        var secondTeamId = Guid.CreateVersion7();
        var firstActorId = Guid.CreateVersion7();
        var secondActorId = Guid.CreateVersion7();
        var thirdActorId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow;
        ScoreboardEntryAllocation[] detailEntries =
        [
            new(firstTeamId, 0,
                new(Guid.CreateVersion7(), ScoreboardEntryKind.Solve, ScoreboardEntryOutcome.Succeeded,
                    0, null, occurredAt, occurredAt, 500, 0, 500, ScoreboardAward.FirstBlood, 25)),
            new(firstTeamId, 0,
                new(Guid.CreateVersion7(), ScoreboardEntryKind.Solve, ScoreboardEntryOutcome.Failed,
                    1, null, occurredAt.AddSeconds(-1), occurredAt, 0, 100, -100)),
            new(firstTeamId, 0,
                new(Guid.CreateVersion7(), ScoreboardEntryKind.Solve, ScoreboardEntryOutcome.Rejected,
                    2, null, occurredAt.AddSeconds(-2), occurredAt, 0, 0, 0))
        ];
        var slot = new ScoreboardSlot(
            0,
            ScoreboardScoreState.Settled,
            500,
            100,
            400,
            3,
            [new(ScoreboardBreakdownKind.Solve, 1, 3, 500, 100, 400)],
            []);
        var projection = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [
                new(firstTeamId, "Alpha", "default", 1, ScoreboardRankingState.Eligible, 400, 0, [], [slot]),
                new(secondTeamId, "Beta", "default", 2, ScoreboardRankingState.Eligible, 400, 0, [], [slot])
            ]);
        projection = projection with
        {
            DetailActors =
            [
                new(0, firstActorId, "First player"),
                new(1, secondActorId, "Second player"),
                new(2, thirdActorId, "Third player")
            ],
            EntryAllocations = detailEntries
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher());
        using var client = app.GetTestClient();
        var route = $"/api/v1/competitions/{competitionId}/leaderboard/teams/{firstTeamId}/columns/0?limit=2";

        using var first = await client.GetAsync(route);
        var page = await first.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page!.Items).Count().IsEqualTo(2);
        await Assert.That(page.Actors.Select(actor => actor.UserId))
            .IsEquivalentTo([firstActorId, secondActorId]);
        await Assert.That(page.Items.Select(item => item.ActorIndex))
            .IsEquivalentTo(new int?[] { 0, 1 });
        await Assert.That(page.NextCursor).IsNotNull();
        await Assert.That(page.EarnedPoints).IsEqualTo(500);
        await Assert.That(page.DeductedPoints).IsEqualTo(100);
        await Assert.That(page.NetPoints).IsEqualTo(400);
        await Assert.That(page.Items[0].EarnedPoints).IsEqualTo(500);
        await Assert.That(page.Items[0].NetPoints).IsEqualTo(500);
        await Assert.That(page.Items[0].Award).IsEqualTo(ScoreboardAwardProtocol.FirstBlood);
        await Assert.That(page.Items[0].AwardPoints).IsEqualTo(25);
        await Assert.That(page.Items[1].DeductedPoints).IsEqualTo(100);
        await Assert.That(page.Items[1].NetPoints).IsEqualTo(-100);

        var cursor = Uri.EscapeDataString(page.NextCursor!);
        using var otherTeam = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{secondTeamId}/columns/0?limit=2&cursor={cursor}");
        var tampered = Uri.EscapeDataString(page.NextCursor![..^1]
            + (page.NextCursor[^1] == 'A' ? "B" : "A"));
        using var tamperedResponse = await client.GetAsync($"{route}&cursor={tampered}");

        await Assert.That(otherTeam.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(tamperedResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Frozen_adjustment_detail_pages_facts_at_the_frozen_cutoff()
    {
        var competitionId = Guid.CreateVersion7();
        var firstTeamId = Guid.CreateVersion7();
        var secondTeamId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow;
        ScoreboardAdjustmentDetailFact[] adjustments =
        [
            new(Guid.CreateVersion7(), actorId, "Operator", occurredAt, 25),
            new(Guid.CreateVersion7(), actorId, "Operator", occurredAt.AddSeconds(-1), -10),
            new(Guid.CreateVersion7(), null, null, occurredAt.AddSeconds(-2), 5)
        ];
        var projection = CreateProjection(
            competitionId,
            [],
            [],
            [
                new(firstTeamId, "Alpha", "default", 1, ScoreboardRankingState.Eligible,
                    20, 3, [], []),
                new(secondTeamId, "Beta", "default", 2, ScoreboardRankingState.Eligible,
                    0, 0, [], [])
            ]);
        projection = projection with
        {
            DetailActors = [new(0, actorId, "Operator")]
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(frozen: true, projection: projection),
            new RecordingMessagePublisher(),
            CompetitionLeaderboardVisibility.Frozen,
            LeaderboardDataScope.Frozen,
            detailReader: new StaticScoreboardDetailReader(adjustments: adjustments));
        using var client = app.GetTestClient();
        var route = $"/api/v1/competitions/{competitionId}/leaderboard/teams/{firstTeamId}/adjustments?limit=2";

        using var first = await client.GetAsync(route);
        var page = await first.Content.ReadFromJsonAsync<ScoreboardAdjustmentDetailResponse>();

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page!.EntryCount).IsEqualTo(3);
        await Assert.That(page.Items.Select(item => item.NetPoints)).IsEquivalentTo([25L, -10L]);
        await Assert.That(page.Items[0].EarnedPoints).IsEqualTo(25L);
        await Assert.That(page.Items[1].DeductedPoints).IsEqualTo(10L);
        await Assert.That(page.Actors.Single().DisplayName).IsEqualTo("Operator");
        await Assert.That(page.NextCursor).IsNotNull();

        var cursor = Uri.EscapeDataString(page.NextCursor!);
        using var otherTeam = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{secondTeamId}/adjustments?limit=2&cursor={cursor}");
        using var second = await client.GetAsync($"{route}&cursor={cursor}");
        var secondPage = await second.Content.ReadFromJsonAsync<ScoreboardAdjustmentDetailResponse>();

        await Assert.That(otherTeam.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondPage!.Items).HasSingleItem();
        await Assert.That(secondPage.NextCursor).IsNull();
    }

    [Test]
    public async Task Slot_detail_expands_grouped_projection_facts_without_changing_authoritative_totals()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow;
        var facts = Enumerable.Range(0, 3)
            .Select(index => new ScoreboardSlotDetailFact(
                Guid.CreateVersion7(occurredAt.AddSeconds(-index)),
                GameplayFactKind.FlagAttempt,
                GameplayFactState.Completed,
                GameplayFactResult.Wrong,
                null,
                null,
                null,
                actorId,
                "Player",
                null,
                true,
                occurredAt.AddSeconds(-index)))
            .ToArray();
        var representative = facts[^1];
        var slot = new ScoreboardSlot(
            0,
            ScoreboardScoreState.Provisional,
            0,
            30,
            -30,
            3,
            [new(ScoreboardBreakdownKind.Solve, 0, 3, 0, 30, -30)],
            []);
        var projection = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [new(teamId, "Alpha", "default", 1, ScoreboardRankingState.Eligible, -30, 0, [], [slot])]);
        projection = projection with
        {
            DetailActors = [new(0, actorId, "Player")],
            EntryAllocations =
            [
                new ScoreboardEntryAllocation(
                    teamId,
                    0,
                    new ScoreboardSlotEntry(
                        representative.Id,
                        ScoreboardEntryKind.Solve,
                        ScoreboardEntryOutcome.Failed,
                        0,
                        null,
                        representative.OccurredAt,
                        null,
                        0,
                        30,
                        -30))
                {
                    Source = new(
                        GameplayFactKind.FlagAttempt,
                        GameplayFactState.Completed,
                        GameplayFactResult.Wrong,
                        null,
                        null,
                        null,
                        null,
                        actorId,
                        3,
                        0,
                        10)
                }
            ]
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher(),
            detailReader: new StaticScoreboardDetailReader(slotFacts: facts));
        using var client = app.GetTestClient();
        var route = $"/api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/columns/0?limit=2";

        using var first = await client.GetAsync(route);
        var firstPage = await first.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();
        using var second = await client.GetAsync(
            $"{route}&cursor={Uri.EscapeDataString(firstPage!.NextCursor!)}");
        var secondPage = await second.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(firstPage.EntryCount).IsEqualTo(3);
        await Assert.That(firstPage.DeductedPoints).IsEqualTo(30);
        await Assert.That(firstPage.Items).Count().IsEqualTo(2);
        await Assert.That(firstPage.Items.All(item => item.DeductedPoints == 10)).IsTrue();
        await Assert.That(secondPage!.Items).HasSingleItem();
        await Assert.That(secondPage.Items[0].DeductedPoints).IsEqualTo(10);
        await Assert.That(firstPage.Items.Concat(secondPage.Items).Select(item => item.Id))
            .IsEquivalentTo(facts.Select(fact => fact.Id));
    }

    [Test]
    public async Task Historical_slot_detail_does_not_guess_between_distinct_awdp_failure_penalties()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var roundId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var fact = new ScoreboardSlotDetailFact(
            Guid.CreateVersion7(occurredAt),
            GameplayFactKind.FixAttempt,
            GameplayFactState.Completed,
            GameplayFactResult.Wrong,
            null,
            null,
            null,
            actorId,
            "Player",
            null,
            false,
            occurredAt);
        var slot = new ScoreboardSlot(
            0,
            ScoreboardScoreState.Settled,
            0,
            30,
            -30,
            2,
            [new(ScoreboardBreakdownKind.Defense, 0, 2, 0, 30, -30)],
            []);
        var projection = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, roundId)],
            [new(teamId, "Alpha", "default", 1, ScoreboardRankingState.Eligible, -30, 0, [], [slot])]);
        projection = projection with
        {
            Schema = projection.Schema with
            {
                Mode = GameMode.Awdp,
                Rounds =
                [
                    new(roundId, 1, occurredAt.AddMinutes(-1), occurredAt.AddMinutes(1),
                        occurredAt.AddMinutes(1), ScoreboardRoundState.Settled)
                ]
            },
            Snapshot = projection.Snapshot with { DataAsOf = occurredAt.AddMinutes(1) },
            EntryAllocations =
            [
                AwdpFailureAllocation(GameplayFactFailureCode.AwdpExploitSucceeded, 10),
                AwdpFailureAllocation(GameplayFactFailureCode.AwdpServiceAbnormal, 20)
            ]
        };

        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher(),
            gameMode: GameMode.Awdp,
            detailReader: new StaticScoreboardDetailReader(slotFacts: [fact]));
        using var response = await app.GetTestClient().GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/columns/0");
        var page = await response.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page!.Items).HasSingleItem();
        await Assert.That(page.Items[0].EarnedPoints).IsNull();
        await Assert.That(page.Items[0].DeductedPoints).IsNull();
        await Assert.That(page.Items[0].NetPoints).IsNull();

        ScoreboardEntryAllocation AwdpFailureAllocation(
            GameplayFactFailureCode failureCode,
            long penalty) => new(
            teamId,
            0,
            new ScoreboardSlotEntry(
                Guid.CreateVersion7(occurredAt.AddTicks(-(int)penalty)),
                ScoreboardEntryKind.Defense,
                ScoreboardEntryOutcome.Failed,
                0,
                null,
                occurredAt,
                occurredAt.AddMinutes(1),
                0,
                penalty,
                -penalty))
            {
                Source = new(
                GameplayFactKind.FixAttempt,
                GameplayFactState.Completed,
                GameplayFactResult.Wrong,
                failureCode,
                null,
                null,
                null,
                actorId,
                1,
                0,
                penalty)
            };
    }

    [Test]
    public async Task Public_snapshot_and_slot_detail_hide_internal_track_teams_and_actors()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var publicTeamId = Guid.CreateVersion7();
        var internalTeamId = Guid.CreateVersion7();
        var publicActorId = Guid.CreateVersion7();
        var internalActorId = Guid.CreateVersion7();
        var publicEntryId = Guid.CreateVersion7();
        var slot = new ScoreboardSlot(
            0,
            ScoreboardScoreState.Provisional,
            1,
            0,
            1,
            1,
            [],
            [new(publicEntryId, ScoreboardEntryKind.Attack, ScoreboardEntryOutcome.Succeeded,
                0, internalTeamId, DateTimeOffset.UtcNow, null, 1, 0, 1)]);
        var internalSlot = slot with
        {
            Entries = [slot.Entries[0] with { Id = Guid.CreateVersion7(), ActorIndex = 1 }]
        };
        var projection = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [
                new(publicTeamId, "Public", "default", 1, ScoreboardRankingState.Eligible, 1, 0, [], [slot]),
                new(internalTeamId, "Internal", "staff", null, ScoreboardRankingState.Disqualified, 1, 0, [], [internalSlot])
            ],
            [
                new(0, publicActorId, "Public player"),
                new(1, internalActorId, "Internal player")
            ]);
        projection = projection with
        {
            DetailActors = projection.Snapshot.Actors,
            EntryAllocations =
            [
                new ScoreboardEntryAllocation(publicTeamId, 0, slot.Entries[0])
                {
                    Source = new(
                        GameplayFactKind.FlagAttempt,
                        GameplayFactState.Completed,
                        GameplayFactResult.Correct,
                        null,
                        null,
                        null,
                        internalTeamId,
                        publicActorId,
                        1,
                        1,
                        0)
                }
            ]
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher(),
            trackStore: new InternalTrackStore(competitionId),
            detailReader: new StaticScoreboardDetailReader(slotFacts:
            [
                new(
                    publicEntryId,
                    GameplayFactKind.FlagAttempt,
                    GameplayFactState.Completed,
                    GameplayFactResult.Correct,
                    null,
                    null,
                    null,
                    publicActorId,
                    "Public player",
                    internalTeamId,
                    true,
                    slot.Entries[0].OccurredAt)
            ]));
        using var client = app.GetTestClient();

        using var leaderboardResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        var leaderboard = await leaderboardResponse.Content.ReadFromJsonAsync<ScoreboardSnapshotResponse>();
        using var detailResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{publicTeamId}/columns/0");
        var detail = await detailResponse.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();
        using var internalDetailResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{internalTeamId}/columns/0");

        await Assert.That(leaderboardResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(leaderboard!.Teams.Select(team => team.TeamId)).IsEquivalentTo([publicTeamId]);
        await Assert.That(leaderboard.Actors.Select(actor => actor.UserId)).IsEquivalentTo([publicActorId]);
        await Assert.That(leaderboard.Teams.Single().Slots.Single().Entries.Single().TargetTeamId).IsNull();
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail!.Items.Single().TargetTeamId).IsNull();
        await Assert.That(internalDetailResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Participant_snapshot_marks_and_includes_only_the_viewers_hidden_track()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var publicTeamId = Guid.CreateVersion7();
        var viewerTeamId = Guid.CreateVersion7();
        var otherHiddenTeamId = Guid.CreateVersion7();
        var projection = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [
                new(publicTeamId, "Public", "default", 1, ScoreboardRankingState.Eligible, 1, 0, [], []),
                new(viewerTeamId, "Viewer", "hidden", null, ScoreboardRankingState.Disqualified, 1, 0, [], []),
                new(otherHiddenTeamId, "Other hidden", "other", null, ScoreboardRankingState.Disqualified, 1, 0, [], [])
            ]);
        projection = projection with
        {
            Snapshot = projection.Snapshot with
            {
                Tracks =
                [
                    new("default", "Default", false, true),
                    new("hidden", "Hidden", true, false),
                    new("other", "Other", true, false)
                ]
            }
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher(),
            trackStore: new ViewerHiddenTrackStore(competitionId, viewerTeamId));

        using var response = await app.GetTestClient().GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        var snapshot = await response.Content.ReadFromJsonAsync<ScoreboardSnapshotResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(snapshot!.Teams.Select(team => team.TeamId))
            .IsEquivalentTo([publicTeamId, viewerTeamId]);
        await Assert.That(snapshot.Tracks.Select(track => track.Key))
            .IsEquivalentTo(["default", "hidden"]);
        await Assert.That(snapshot.Tracks.Single(track => track.Key == "default").IsViewerTrack)
            .IsFalse();
        await Assert.That(snapshot.Tracks.Single(track => track.Key == "hidden").IsViewerTrack)
            .IsTrue();
    }

    [Test]
    public async Task Frozen_participant_snapshot_uses_the_frozen_team_track_after_reassignment()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var publicTeamId = Guid.CreateVersion7();
        var viewerTeamId = Guid.CreateVersion7();
        var slot = new ScoreboardSlot(
            0, ScoreboardScoreState.Settled, 1, 0, 1, 1, [], []);
        var projection = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [
                new(publicTeamId, "Public", "default", 1, ScoreboardRankingState.Eligible, 1, 0, [], []),
                new(viewerTeamId, "Viewer", "hidden", null, ScoreboardRankingState.Disqualified,
                    1, 0, [], [slot])
            ]);
        projection = projection with
        {
            Snapshot = projection.Snapshot with
            {
                Visibility = CompetitionLeaderboardVisibility.Frozen,
                DataScope = LeaderboardDataScope.Frozen,
                Tracks =
                [
                    new("default", "Default", false, true),
                    new("hidden", "Hidden", true, false),
                    new("other", "Other", true, false)
                ]
            }
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(frozen: true, projection: projection),
            new RecordingMessagePublisher(),
            CompetitionLeaderboardVisibility.Frozen,
            LeaderboardDataScope.Frozen,
            trackStore: new ViewerHiddenTrackStore(competitionId, viewerTeamId, "other"));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        var snapshot = await response.Content.ReadFromJsonAsync<ScoreboardSnapshotResponse>();
        using var detailResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{viewerTeamId}/columns/0");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(snapshot!.Teams.Select(team => team.TeamId))
            .IsEquivalentTo([publicTeamId, viewerTeamId]);
        await Assert.That(snapshot.Tracks.Select(track => track.Key))
            .IsEquivalentTo(["default", "hidden"]);
        await Assert.That(snapshot.Tracks.Single(track => track.Key == "hidden").IsViewerTrack)
            .IsTrue();
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Slot_detail_cursor_is_invalidated_when_frozen_snapshot_identity_changes()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow;
        var slot = new ScoreboardSlot(
            0, ScoreboardScoreState.Settled, 1, 0, 1, 2, [], []);
        var first = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [new(teamId, "Alpha", "default", 1, ScoreboardRankingState.Eligible, 1, 0, [], [slot])]);
        first = first with
        {
            Snapshot = first.Snapshot with { DataAsOf = occurredAt, Version = 17 },
            EntryAllocations =
            [
                new(teamId, 0,
                    new(Guid.CreateVersion7(), ScoreboardEntryKind.Solve,
                        ScoreboardEntryOutcome.Succeeded, null, null, occurredAt, occurredAt,
                        1, 0, 1)),
                new(teamId, 0,
                    new(Guid.CreateVersion7(), ScoreboardEntryKind.Solve,
                        ScoreboardEntryOutcome.Failed, null, null, occurredAt.AddSeconds(-1), occurredAt,
                        0, 0, 0))
            ]
        };
        var second = first with
        {
            Snapshot = first.Snapshot with
            {
                DataAsOf = occurredAt.AddSeconds(1),
                Version = 18
            }
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new SwitchingLeaderboard(first, second),
            new RecordingMessagePublisher(),
            CompetitionLeaderboardVisibility.Frozen,
            LeaderboardDataScope.Frozen);
        using var client = app.GetTestClient();
        var route = $"/api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/columns/0?limit=1";

        using var firstResponse = await client.GetAsync(route);
        var page = await firstResponse.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();
        using var secondResponse = await client.GetAsync(
            $"{route}&cursor={Uri.EscapeDataString(page!.NextCursor!)}");

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page.NextCursor).IsNotNull();
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        Guid competitionId,
        ILeaderboardCache leaderboard,
        IBackendMessagePublisher messages,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live,
        GameMode gameMode = GameMode.Ctf,
        ICompetitionTrackStore? trackStore = null,
        IScoreboardDetailReader? detailReader = null,
        ILeaderboardSnapshotFactory? snapshotFactory = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Pagination:SigningKey"] = "leaderboard-endpoint-tests-signing-key"
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetLeaderboardEndpoint).Assembly];
            options.Filter = type => type == typeof(GetLeaderboardEndpoint)
                || type == typeof(GetScoreboardChallengeCatalogEndpoint)
                || type == typeof(GetScoreboardSchemaEndpoint)
                || type == typeof(GetLeaderboardValidator)
                || type == typeof(GetScoreboardSchemaValidator)
                || type == typeof(GetScoreboardSlotDetailEndpoint)
                || type == typeof(GetScoreboardSlotDetailValidator)
                || type == typeof(GetScoreboardAdjustmentDetailEndpoint)
                || type == typeof(GetScoreboardAdjustmentDetailValidator);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddSingleton(leaderboard);
        builder.Services.AddSingleton(snapshotFactory ?? Substitute.For<ILeaderboardSnapshotFactory>());
        builder.Services.AddSingleton(messages);
        builder.Services.AddSingleton<ICompetitionVisibilityAccess>(
            new PublicVisibilityAccess(competitionId, gameMode, visibility, dataScope));
        builder.Services.AddSingleton<GetCompetitionTracks>();
        builder.Services.AddSingleton<ICompetitionTrackStore>(
            trackStore ?? new DefaultTrackStore(competitionId));
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(new NoStaffAccess());
        builder.Services.AddSingleton<IUserContext>(new AnonymousUserContext());
        builder.Services.AddSingleton(detailReader ?? new StaticScoreboardDetailReader());
        builder.Services.AddSingleton<NoCTF.API.Pagination.SignedKeysetCursor>();

        var app = builder.Build();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class PublicVisibilityAccess(
        Guid competitionId,
        GameMode gameMode,
        CompetitionLeaderboardVisibility visibility,
        LeaderboardDataScope dataScope)
        : ICompetitionVisibilityAccess
    {
        public Task<CompetitionVisibilityAccessDecision?> ResolveAsync(
            Guid userId,
            Guid requestedCompetitionId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionVisibilityAccessDecision?>(
                requestedCompetitionId == competitionId
                    ? new(
                        gameMode,
                        NoCTF.Domain.Competitions.CompetitionStatus.Running,
                        visibility,
                        dataScope,
                        2)
                    : null);
    }

    private sealed class AnonymousUserContext : IUserContext
    {
        public Guid UserId => Guid.Empty;
        public bool IsAdministrator => false;
    }

    private sealed class DefaultTrackStore(Guid competitionId) : ICompetitionTrackStore
    {
        public Task<CompetitionTracksView?> GetAsync(
            Guid requestedCompetitionId,
            Guid? viewerUserId,
            bool includeInternal,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionTracksView?>(requestedCompetitionId == competitionId
                ? new CompetitionTracksView(
                    competitionId,
                    GameMode.Ctf,
                    CompetitionStatus.Running,
                    0,
                    true,
                    [new CompetitionTrackView(
                        CompetitionTrackConfiguration.DefaultTrackKey,
                        "Default",
                        true,
                        true,
                        false,
                        true,
                        true,
                        true,
                        true,
                        true)])
                : null);

        public Task<NoCTF.Application.Common.OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>> UpdateAsync(
            UpdateCompetitionTracksCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoCTF.Application.Common.OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
            AssignTeamTrackCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoStaffAccess : ICompetitionModerationAuthorizer
    {
        public Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> CanJudgeAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> CanObserveAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> CanReadHistoricalAuditAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class CachedLeaderboard(
        bool missing = false,
        bool frozen = false,
        DateTimeOffset? lastFailureAt = null,
        ScoreboardProjection? projection = null) : ILeaderboardCache
    {
        public List<Guid> InvalidatedCompetitionIds { get; } = [];

        public Task<LeaderboardResponse?> GetAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(
                missing
                    ? null
                    : new(competitionId, DateTimeOffset.UtcNow, []));

        public Task<LeaderboardResponse?> GetFrozenAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(frozen
                ? new LeaderboardResponse(
                    competitionId,
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    [])
                {
                    Visibility = CompetitionLeaderboardVisibility.Frozen,
                    DataScope = LeaderboardDataScope.Frozen,
                    DataAsOf = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
                : null);

        public Task<ScoreboardProjection?> GetScoreboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScoreboardProjection?>(missing
                ? null
                : EnsureParticipantView(projection ?? CreateScoreboard(competitionId, frozen: false)));

        public Task<ScoreboardProjection?> GetFrozenScoreboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScoreboardProjection?>(frozen
                ? EnsureParticipantView(projection ?? CreateScoreboard(competitionId, frozen: true))
                : null);

        public Task<LeaderboardCacheStatus> GetStatusAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LeaderboardCacheStatus(lastFailureAt));

        public Task RefreshAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(
            Guid competitionId,
            CancellationToken cancellationToken)
        {
            InvalidatedCompetitionIds.Add(competitionId);
            return Task.CompletedTask;
        }

        private static ScoreboardProjection CreateScoreboard(Guid competitionId, bool frozen)
        {
            var generatedAt = DateTimeOffset.UtcNow.AddMinutes(frozen ? -5 : 0);
            return new ScoreboardProjection(
                new ScoreboardChallengeCatalog(competitionId, 1, []),
                new ScoreboardSchema(competitionId, GameMode.Ctf, 1, 1, [], []),
                new ScoreboardSnapshot(competitionId, 1, 1, generatedAt, null, [], [])
                {
                    Visibility = frozen
                        ? CompetitionLeaderboardVisibility.Frozen
                        : CompetitionLeaderboardVisibility.Normal,
                    DataScope = frozen ? LeaderboardDataScope.Frozen : LeaderboardDataScope.Live,
                    DataAsOf = generatedAt
                });
        }
    }

    private sealed class StaticScoreboardDetailReader(
        IReadOnlyList<ScoreboardSlotDetailFact>? slotFacts = null,
        IReadOnlyList<ScoreboardAdjustmentDetailFact>? adjustments = null)
        : IScoreboardDetailReader
    {
        public Task<IReadOnlyList<ScoreboardSlotDetailFact>> ReadSlotAsync(
            ScoreboardSlotDetailQuery query,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ScoreboardSlotDetailFact>>(
            (slotFacts ?? [])
            .Where(fact => query.BeforeOccurredAt is not DateTimeOffset beforeAt
                || fact.OccurredAt < beforeAt
                || fact.OccurredAt == beforeAt && fact.Id.CompareTo(query.BeforeId!.Value) < 0)
            .OrderByDescending(fact => fact.OccurredAt)
            .ThenByDescending(fact => fact.Id)
            .Take(query.Limit)
            .ToArray());

        public Task<IReadOnlyList<ScoreboardAdjustmentDetailFact>> ReadAdjustmentsAsync(
            ScoreboardAdjustmentDetailQuery query,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ScoreboardAdjustmentDetailFact>>(
            (adjustments ?? [])
            .Where(item => query.BeforeOccurredAt is not DateTimeOffset beforeAt
                || item.OccurredAt < beforeAt
                || item.OccurredAt == beforeAt && item.Id.CompareTo(query.BeforeId!.Value) < 0)
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(query.Limit)
            .ToArray());
    }

    private sealed class SwitchingLeaderboard(
        ScoreboardProjection first,
        ScoreboardProjection second) : ILeaderboardCache
    {
        private int reads;

        public Task<ScoreboardProjection?> GetFrozenScoreboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScoreboardProjection?>(EnsureParticipantView(
                Interlocked.Increment(ref reads) == 1
                ? first
                : second));

        public Task<LeaderboardResponse?> GetFrozenAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<LeaderboardResponse?>(null);

        public Task<ScoreboardProjection?> GetScoreboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<ScoreboardProjection?>(null);

        public Task<LeaderboardResponse?> GetAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<LeaderboardResponse?>(null);

        public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InternalTrackStore(Guid competitionId) : ICompetitionTrackStore
    {
        public Task<CompetitionTracksView?> GetAsync(
            Guid requestedCompetitionId,
            Guid? viewerUserId,
            bool includeInternal,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionTracksView?>(requestedCompetitionId == competitionId
                ? new CompetitionTracksView(
                    competitionId,
                    GameMode.Ctf,
                    CompetitionStatus.Running,
                    1,
                    false,
                    [
                        new("default", "Default", true, true, false, true, true, true, true, true),
                        new("staff", "Staff", false, false, true, false, false, false, false, false)
                    ])
                : null);

        public Task<NoCTF.Application.Common.OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>> UpdateAsync(
            UpdateCompetitionTracksCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoCTF.Application.Common.OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
            AssignTeamTrackCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ViewerHiddenTrackStore(
        Guid competitionId,
        Guid viewerTeamId,
        string viewerTrackKey = "hidden")
        : ICompetitionTrackStore
    {
        public Task<CompetitionTracksView?> GetAsync(
            Guid requestedCompetitionId,
            Guid? viewerUserId,
            bool includeInternal,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionTracksView?>(requestedCompetitionId == competitionId
                ? new CompetitionTracksView(
                    competitionId,
                    GameMode.Ctf,
                    CompetitionStatus.Running,
                    1,
                    false,
                    [
                        new("default", "Default", true, true, false, true, true, true, true, true),
                        new("hidden", "Hidden", false, false, true, true, true, true, false, true,
                            string.Equals(viewerTrackKey, "hidden", StringComparison.OrdinalIgnoreCase)),
                        new("other", "Other", false, false, true, true, true, true, false, true,
                            string.Equals(viewerTrackKey, "other", StringComparison.OrdinalIgnoreCase))
                    ],
                    viewerTeamId)
                : null);

        public Task<NoCTF.Application.Common.OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>> UpdateAsync(
            UpdateCompetitionTracksCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoCTF.Application.Common.OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
            AssignTeamTrackCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static ScoreboardProjection CreateProjection(
        Guid competitionId,
        IReadOnlyList<ScoreboardChallengeCatalogItem> challenges,
        IReadOnlyList<ScoreboardColumn> columns,
        IReadOnlyList<ScoreboardTeam> teams,
        IReadOnlyList<ScoreboardActor>? actors = null) => new(
        new ScoreboardChallengeCatalog(competitionId, 9, challenges),
        new ScoreboardSchema(competitionId, GameMode.Ctf, 11, 9, [], columns),
        new ScoreboardSnapshot(competitionId, 17, 11, DateTimeOffset.UtcNow, null, actors ?? [], teams));

    private static ScoreboardProjection EnsureParticipantView(ScoreboardProjection projection) =>
        projection.ParticipantView is not null
            ? projection
            : projection with
            {
                ParticipantView = ScoreboardAudienceView.From(projection)
            };

    private sealed class RecordingMessagePublisher : IBackendMessagePublisher
    {
        public List<Guid> ProjectedCompetitionIds { get; } = [];

        public ValueTask ProjectLeaderboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken)
        {
            ProjectedCompetitionIds.Add(competitionId);
            return ValueTask.CompletedTask;
        }

        public ValueTask RebuildCompetitionAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CleanupCompetitionRuntimesAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ProvisionCompetitionRuntimesAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

}
