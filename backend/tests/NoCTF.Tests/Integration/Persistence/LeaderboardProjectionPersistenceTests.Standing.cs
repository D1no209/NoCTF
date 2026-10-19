using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using NATS.Net;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Standings;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Tracks;
using NoCTF.Infrastructure.Competitions.Visibility;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Infrastructure.Teams.Standings;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Wolverine;
using Wolverine.Nats;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class LeaderboardProjectionPersistenceTests
{
    [Test, Timeout(300_000), NotInParallel]
    public Task Standing_reads_real_published_and_frozen_scores_and_rechecks_membership(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await using var redis = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
        await using var nats = new ContainerBuilder("nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
            .WithPortBinding(4222, true).WithCommand("-js")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
        await Task.WhenAll(postgres.StartAsync(ct), redis.StartAsync(ct), nats.StartAsync(ct));
        var natsUrl = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
        using var messaging = Host.CreateDefaultBuilder().UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.UseNats(natsUrl).AutoProvision().UseJetStream(_ => { })
                .DefineStream("STANDING_TEST", stream =>
                {
                    stream.WithSubject("standing.test");
                    stream.Retention = NATS.Client.JetStream.Models.StreamConfigRetention.Workqueue;
                });
            options.PublishMessage<ProjectLeaderboard>().ToNatsSubject("standing.test").UseJetStream("STANDING_TEST");
        }).Build();
        await messaging.StartAsync(ct);
        await using var connection = new NatsConnection(new NatsOpts { Url = natsUrl });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Redis"] = redis.GetConnectionString() }).Build();
        await using var services = new ServiceCollection().AddNoCtfCaching(configuration, false).BuildServiceProvider();
        var caches = services.GetRequiredService<IFusionCacheProvider>();
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var owner = CreateUser(now);
        var participant = CreateUser(now.AddTicks(1));
        participant.Role = NoCTF.Domain.Identity.UserRole.User;
        participant.UserName = "participant"; participant.NormalizedUserName = "PARTICIPANT";
        participant.Email = "participant@example.test"; participant.NormalizedEmail = "PARTICIPANT@EXAMPLE.TEST";
        var fixture = CreateFixture(GameMode.Ctf, 0, participant.Id, now);
        fixture.Competition.OwnerId = owner.Id;
        fixture.Challenge.OwnerId = owner.Id;
        fixture.Facts[0].Result = GameplayFactResult.Correct;
        fixture.CompetitionChallenge.Rules!.HasScoreCurve = true;
        ((CtfCompetitionChallengeRules)fixture.CompetitionChallenge.Rules!).ScoreCurve = new()
            { InitialPoints = 100, MinimumPoints = 100, DecayTeamCount = 10, DecayMode = PersistedScoreDecayMode.Fixed };
        db.Users.AddRange(owner, participant); db.Competitions.Add(fixture.Competition); db.Challenges.Add(fixture.Challenge);
        db.CompetitionChallenges.Add(fixture.CompetitionChallenge); db.Teams.Add(fixture.Team); db.GameplayFacts.Add(fixture.Facts[0]);
        await db.SaveChangesAsync(ct);
        var bus = messaging.Services.GetRequiredService<IMessageBus>();
        var outbox = new WolverinePostCommitMessagePublisher(bus, db);
        var events = new CompetitionEventStore(db, outbox);
        var fence = new NatsLeaderboardPublicationFence(connection, caches);
        var cache = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            new NatsLeaderboardRefreshPublisher(connection), caches, fence, workPublisher: outbox);
        var query = new GetMyTeamStanding(new MyTeamStandingIdentityReader(db), new CompetitionVisibilityAccess(db), cache,
            new(new CompetitionTrackStore(db, outbox, events)), new CompetitionModerationAuthorizer(db),
            new WolverineBackendMessagePublisher(bus), TimeProvider.System);
        async Task<MyTeamStandingResult> Read() => await query.ExecuteAsync(fixture.Competition.Id, participant.Id, ct);
        await Assert.That((await Read()).State).IsEqualTo(MyTeamStandingState.Processing);
        await Assert.That((await Read()).State).IsEqualTo(MyTeamStandingState.Processing);
        var pendingStream = await connection.CreateJetStreamContext().GetStreamAsync("STANDING_TEST", cancellationToken: ct);
        for (var attempt = 0; pendingStream.Info.State.Messages < 2 && attempt < 50; attempt++)
        {
            await Task.Delay(100, ct);
            pendingStream = await connection.CreateJetStreamContext().GetStreamAsync("STANDING_TEST", cancellationToken: ct);
        }
        await Assert.That(pendingStream.Info.State.Messages).IsGreaterThanOrEqualTo(2);
        await cache.RefreshAsync(fixture.Competition.Id, ct);
        var live = await Read();
        await Assert.That(live.State).IsEqualTo(MyTeamStandingState.Ready);
        await Assert.That(live.Standing!.TeamId).IsEqualTo(fixture.Team.Id);
        await Assert.That(live.Standing.TotalScore).IsEqualTo(100L);
        fixture.Team.RegistrationStatus = TeamRegistrationStatus.Pending; await db.SaveChangesAsync(ct);
        await Assert.That((await Read()).State).IsEqualTo(MyTeamStandingState.NotFound);
        fixture.Team.RegistrationStatus = TeamRegistrationStatus.Approved; fixture.Team.IsBanned = true; await db.SaveChangesAsync(ct);
        await Assert.That((await Read()).State).IsEqualTo(MyTeamStandingState.NotFound);
        fixture.Team.IsBanned = false; fixture.Competition.HiddenStartAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(ct);
        var hidden = await Read();
        await Assert.That(hidden.Standing!.TotalScore).IsNull();
        await Assert.That(hidden.Standing.ChallengeBenefits).IsEmpty();
        fixture.Competition.HiddenStartAt = null;
        fixture.Competition.FrozenStartAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        _ = await cache.GetFrozenWebhookScoreboardAsync(fixture.Competition.Id, fixture.Competition.FrozenStartAt.Value, ct);
        var frozen = await Read();
        await Assert.That(frozen.Standing!.TotalScore).IsEqualTo(100L);
        await Assert.That(frozen.Standing.DataScope).IsEqualTo(NoCTF.Application.Scoring.Leaderboard.LeaderboardDataScope.Frozen);
        fixture.Facts[0].Result = GameplayFactResult.Wrong; await db.SaveChangesAsync(ct);
        await Assert.That((await Read()).Standing!.TotalScore).IsEqualTo(100L);
        fixture.Team.TrackKey = "internal";
        await db.SaveChangesAsync(ct);
        var moved = await cache.GetFrozenScoreboardAsync(fixture.Competition.Id, ct);
        await Assert.That(moved!.ParticipantView!.Snapshot.Teams.Single().TrackKey).IsEqualTo("internal");
        await Assert.That(moved.ParticipantView.Snapshot.Teams.Single().TotalScore).IsEqualTo(100L);
        fixture.CompetitionChallenge.IsPublished = false; await db.SaveChangesAsync(ct);
        var restricted = await cache.GetFrozenScoreboardAsync(fixture.Competition.Id, ct);
        await Assert.That(restricted!.ParticipantView!.ChallengeCatalog.Challenges).IsEmpty();
        await Assert.That(restricted.ParticipantView.Snapshot.Teams.Single().Slots).IsEmpty();
        await Assert.That(restricted.ParticipantView.Snapshot.Actors).IsEmpty();
        fixture.Team.IsBanned = true; await db.SaveChangesAsync(ct);
        var banned = await cache.GetFrozenSpectatorScoreboardAsync(fixture.Competition.Id, ct);
        await Assert.That(banned!.ParticipantView!.Snapshot.Teams).IsEmpty();
        await messaging.StopAsync(ct);
    });
}
