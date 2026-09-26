using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Webhooks;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionWebhooks")]
public sealed class CompetitionWebhookBloodReadinessTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Every_blood_rank_uses_complete_live_or_frozen_resources_and_blackout_is_suppressed(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_webhook_blood")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.UtcNow;
            var clock = new FakeTimeProvider(now);
            var competitionId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            var challengeId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var protector = new PlatformSecretProtector(Options.Create(
                new EmailVerificationProtectionOptions
                {
                    EncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32)
                        .Select(value => (byte)value).ToArray())
                }));
            await using var db = new NoCtfDbContext(options, clock);
            await db.Database.EnsureCreatedAsync(ct);
            db.Users.Add(new User
            {
                Id = ownerId, UserName = "blood-owner",
                Email = "blood-owner@example.test", PasswordHash = "test",
                Kind = UserKind.Human, CreatedAt = now, UpdatedAt = now
            });
            var competition = new CtfCompetition
            {
                Id = competitionId, OwnerId = ownerId, Title = "Blood webhook",
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                FlagDerivationSecret = new byte[32], StartAt = now,
                EndAt = now.AddHours(1), Status = CompetitionStatus.Running,
                CreatedAt = now, UpdatedAt = now
            };
            competition.WebhookTargets.Add(new CompetitionWebhookTarget
            {
                Id = targetId, CompetitionId = competitionId, Name = "Receiver",
                EndpointUrl = "https://hooks.example.test/noctf",
                Enabled = true, EnabledAt = now,
                CurrentSecretCiphertext = protector.Protect(
                    "whsec_" + Convert.ToBase64String(new byte[32]),
                    PlatformSecretPurpose.CompetitionWebhookSecret,
                    competitionId, targetId),
                CreatedAt = now, UpdatedAt = now
            });
            db.Competitions.Add(competition);
            await db.SaveChangesAsync(ct);

            var leaderboard = Substitute.For<ILeaderboardCache>();
            var store = new CompetitionWebhookDeliveryStore(
                db, protector,
                new GetChallenge(Substitute.For<IChallengeManagementStore>()),
                new GetCompetitionTracks(Substitute.For<ICompetitionTrackStore>()),
                leaderboard,
                new CompetitionWebhookOptions(new Uri("https://noctf.example.test/"),
                    10, new HashSet<string>(), new HashSet<string>()),
                clock);
            foreach (var kind in new[]
            {
                CompetitionEventKind.FirstBloodAwarded,
                CompetitionEventKind.SecondBloodAwarded,
                CompetitionEventKind.ThirdBloodAwarded
            })
            foreach (var scope in new[]
            {
                LeaderboardDataScope.Live,
                LeaderboardDataScope.Frozen,
                LeaderboardDataScope.Hidden
            })
            {
                competition.FrozenStartAt = scope == LeaderboardDataScope.Frozen
                    ? clock.GetUtcNow().AddMinutes(-1) : null;
                competition.HiddenStartAt = scope == LeaderboardDataScope.Hidden
                    ? clock.GetUtcNow().AddMinutes(-1) : null;
                await db.SaveChangesAsync(ct);
                var eventId = Guid.NewGuid();
                var source = NewBloodEvent(kind);
                source.Id = eventId;
                source.CompetitionId = competitionId;
                source.TeamId = teamId;
                source.CompetitionChallengeId = challengeId;
                source.SubjectType = EntityReferenceKind.Team;
                source.SubjectId = teamId;
                source.Visibility = CompetitionEventVisibility.Public;
                source.LeaderboardVisibility = scope switch
                {
                    LeaderboardDataScope.Live => CompetitionLeaderboardVisibility.Normal,
                    LeaderboardDataScope.Frozen => CompetitionLeaderboardVisibility.Frozen,
                    _ => CompetitionLeaderboardVisibility.Blackout
                };
                source.FrozenStartAt = competition.FrozenStartAt;
                source.OccurredAt = clock.GetUtcNow();
                db.CompetitionEvents.Add(source);
                var outbox = new CompetitionWebhookOutboxEvent
                {
                    EventId = eventId, CompetitionId = competitionId,
                    CompetitionRevision = competition.ConcurrencyStamp,
                    DomainEventCreatedAt = source.OccurredAt,
                    OutboxPersistedAt = clock.GetUtcNow(),
                    NextDispatchAt = clock.GetUtcNow()
                };
                db.CompetitionWebhookOutboxEvents.Add(outbox);
                await db.SaveChangesAsync(ct);
                var batch = await store.PrepareBatchAsync(
                    new(competitionId, eventId), 10, ct);
                if (scope == LeaderboardDataScope.Hidden)
                {
                    await Assert.That(batch.Deliveries).IsEmpty();
                    await Assert.That(await db.CompetitionWebhookDeliveries
                        .AnyAsync(item => item.EventId == eventId, ct)).IsFalse();
                    clock.Advance(TimeSpan.FromSeconds(1));
                    continue;
                }
                var command = batch.Deliveries.Single();

                if (scope == LeaderboardDataScope.Frozen)
                    leaderboard.GetFrozenWebhookScoreboardAsync(
                            competitionId, competition.FrozenStartAt!.Value,
                            Arg.Any<CancellationToken>())
                        .Returns((WebhookScoreboardProjection?)null);
                else
                    leaderboard.GetWebhookScoreboardAsync(
                            competitionId, false, Arg.Any<CancellationToken>())
                        .Returns((WebhookScoreboardProjection?)null);
                await Assert.That(async () =>
                        await store.PrepareDeliveryAsync(command, ct))
                    .Throws<CompetitionWebhookProjectionNotReadyException>();
                await store.RecordProjectionWaitAsync(command,
                    CompetitionWebhookProjectionFailure.MissingPublicProjection,
                    clock.GetUtcNow(), ct);
                clock.Advance(TimeSpan.FromMilliseconds(150));

                if (kind == CompetitionEventKind.FirstBloodAwarded
                    && scope == LeaderboardDataScope.Live)
                {
                    var stale = Projection(competitionId, challengeId, teamId,
                        clock.GetUtcNow(), null);
                    leaderboard.GetWebhookScoreboardAsync(competitionId, false,
                            Arg.Any<CancellationToken>())
                        .Returns(new WebhookScoreboardProjection(stale, outbox.Sequence - 1));
                    await Assert.That(async () =>
                            await store.PrepareDeliveryAsync(command, ct))
                        .Throws<CompetitionWebhookProjectionNotReadyException>();
                    await store.RecordProjectionWaitAsync(command,
                        CompetitionWebhookProjectionFailure.MissingPublicProjection,
                        clock.GetUtcNow(), ct);
                    clock.Advance(TimeSpan.FromMilliseconds(300));

                    var noChallenge = Projection(competitionId, challengeId, teamId,
                        clock.GetUtcNow(), null, includeChallenge: false);
                    leaderboard.GetWebhookScoreboardAsync(competitionId, false,
                            Arg.Any<CancellationToken>())
                        .Returns(new WebhookScoreboardProjection(noChallenge, outbox.Sequence));
                    await Assert.That(async () =>
                            await store.PrepareDeliveryAsync(command, ct))
                        .Throws<CompetitionWebhookProjectionNotReadyException>();
                    await store.RecordProjectionWaitAsync(command,
                        CompetitionWebhookProjectionFailure.MissingPublicChallenge,
                        clock.GetUtcNow(), ct);
                    clock.Advance(TimeSpan.FromMilliseconds(600));

                    var noTeam = Projection(competitionId, challengeId, teamId,
                        clock.GetUtcNow(), null, includeTeam: false);
                    leaderboard.GetWebhookScoreboardAsync(competitionId, false,
                            Arg.Any<CancellationToken>())
                        .Returns(new WebhookScoreboardProjection(noTeam, outbox.Sequence));
                    await Assert.That(async () =>
                            await store.PrepareDeliveryAsync(command, ct))
                        .Throws<CompetitionWebhookProjectionNotReadyException>();
                    await store.RecordProjectionWaitAsync(command,
                        CompetitionWebhookProjectionFailure.MissingPublicTeam,
                        clock.GetUtcNow(), ct);
                    clock.Advance(TimeSpan.FromMilliseconds(1100));
                }

                var projection = Projection(competitionId, challengeId, teamId,
                    clock.GetUtcNow(), competition.FrozenStartAt);
                if (scope == LeaderboardDataScope.Frozen)
                    leaderboard.GetFrozenWebhookScoreboardAsync(
                            competitionId, competition.FrozenStartAt!.Value,
                            Arg.Any<CancellationToken>())
                        .Returns(new WebhookScoreboardProjection(projection, outbox.Sequence));
                else
                    leaderboard.GetWebhookScoreboardAsync(
                            competitionId, false, Arg.Any<CancellationToken>())
                        .Returns(new WebhookScoreboardProjection(projection, outbox.Sequence));
                var prepared = await store.PrepareDeliveryAsync(command, ct);
                await Assert.That(prepared.State)
                    .IsEqualTo(CompetitionWebhookDeliveryReadState.Ready);
                using var body = JsonDocument.Parse(prepared.Body!);
                var data = body.RootElement.GetProperty("data");
                var resources = data.GetProperty("resources");
                var challenge = resources.GetProperty("challenge");
                var snapshot = resources.GetProperty("leaderboard");
                await Assert.That(body.RootElement.GetProperty("id").GetGuid())
                    .IsEqualTo(eventId);
                await Assert.That(body.RootElement.GetProperty("time").GetDateTimeOffset())
                    .IsEqualTo(source.OccurredAt);
                await Assert.That(challenge.GetProperty("id").GetGuid())
                    .IsEqualTo(challengeId);
                await Assert.That(snapshot.GetProperty("dataScope").GetString())
                    .IsEqualTo(scope.ToString());
                await Assert.That(resources.GetProperty("competition")
                        .GetProperty("leaderboardVisibility").GetString())
                    .IsEqualTo(scope == LeaderboardDataScope.Frozen ? "Frozen" : "Normal");
                await Assert.That(challenge.GetProperty("title").GetString())
                    .IsEqualTo("Projected challenge");
                var teams = snapshot.GetProperty("teams").EnumerateArray().ToArray();
                await Assert.That(teams.Any(team =>
                    team.GetProperty("teamId").GetGuid() == teamId
                    && team.GetProperty("teamName").GetString() == "Projected team"))
                    .IsTrue();
                await Assert.That(challenge.GetProperty("projectionVersion").GetString())
                    .IsEqualTo(snapshot.GetProperty("projectionVersion").GetString());
                if (kind == CompetitionEventKind.FirstBloodAwarded
                    && scope == LeaderboardDataScope.Live)
                {
                    var firstCapturedAt = data.GetProperty("capturedAt").GetDateTimeOffset();
                    await store.RecordHttpStartedAsync(command, clock.GetUtcNow(), ct);
                    await store.RecordHttpFailureAsync(command, 503, null,
                        permanent: false, clock.GetUtcNow(), ct);
                    clock.Advance(TimeSpan.FromSeconds(6));
                    var retry = await store.PrepareDeliveryAsync(command, ct);
                    using var retriedBody = JsonDocument.Parse(retry.Body!);
                    await Assert.That(retriedBody.RootElement.GetProperty("id").GetGuid())
                        .IsEqualTo(eventId);
                    await Assert.That(retriedBody.RootElement.GetProperty("time").GetDateTimeOffset())
                        .IsEqualTo(source.OccurredAt);
                    await Assert.That(retriedBody.RootElement.GetProperty("data")
                            .GetProperty("capturedAt").GetDateTimeOffset() > firstCapturedAt)
                        .IsTrue();
                }
                await store.RecordHttpCompletedAsync(command,
                    new(CompetitionWebhookSendResult.Delivered, 204),
                    clock.GetUtcNow(), ct);
                clock.Advance(TimeSpan.FromSeconds(1));
            }
        });
    }

    private static CompetitionEvent NewBloodEvent(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.FirstBloodAwarded => new FirstBloodAwardedEvent(),
        CompetitionEventKind.SecondBloodAwarded => new SecondBloodAwardedEvent(),
        CompetitionEventKind.ThirdBloodAwarded => new ThirdBloodAwardedEvent(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static ScoreboardProjection Projection(
        Guid competitionId, Guid challengeId, Guid teamId,
        DateTimeOffset now, DateTimeOffset? frozenAt,
        bool includeChallenge = true, bool includeTeam = true)
    {
        ScoreboardChallengeCatalogItem[] challenges = includeChallenge
            ? [new(challengeId, "Projected challenge", "Web", "Web", 1, true)]
            : [];
        var catalog = new ScoreboardChallengeCatalog(competitionId, 1, challenges);
        var schema = new ScoreboardSchema(competitionId, GameMode.Ctf, 1, 1, [], []);
        ScoreboardTeam[] teams = includeTeam
            ? [new ScoreboardTeam(teamId, "Projected team", "default", 1,
                ScoreboardRankingState.Eligible, 100, 0, [], [])]
            : [];
        var snapshot = new ScoreboardSnapshot(competitionId, 1, 1, now, null, [], teams)
        {
            DataAsOf = frozenAt ?? now
        };
        var publicProjection = new ScoreboardProjection(catalog, schema, snapshot);
        return publicProjection with
        {
            ParticipantView = ScoreboardAudienceView.From(publicProjection)
        };
    }
}
