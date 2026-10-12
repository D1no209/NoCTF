using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions.Events;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Competitions.Visibility;
using ZiggyCreatures.Caching.Fusion;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class SpectatorFramePersistenceTests
{
    [Test]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Koh)]
    [Timeout(300_000)]
    public async Task Reads_current_state_without_replaying_or_leaking_future_facts(GameMode mode, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("spectator").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options);
            await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
            var userId = Guid.NewGuid(); var competitionId = Guid.NewGuid(); var challengeId = Guid.NewGuid();
            var instanceId = Guid.NewGuid(); var teamId = Guid.NewGuid(); var exhibitionId = Guid.NewGuid(); var defenderId = Guid.NewGuid();
            db.Users.Add(new User { Id = userId, UserName = "spectator", NormalizedUserName = "SPECTATOR", Email = "test@example.test", PasswordHash = "test", Kind = UserKind.Human, Role = UserRole.Administrator, CreatedAt = now, UpdatedAt = now });
            Competition competition = mode == GameMode.Awd ? new AwdCompetition() : new KohCompetition();
            competition.Id = competitionId; competition.OwnerId = userId; competition.Title = "Broadcast";
            competition.ModeConfiguration = TestConfigurations.Competition(mode); competition.FlagDerivationSecret = new byte[32];
            competition.StartAt = now.AddHours(-1); competition.EndAt = now.AddHours(1); competition.Status = CompetitionStatus.Running;
            competition.CreatedAt = now; competition.UpdatedAt = now; db.Competitions.Add(competition);
            Challenge challenge = mode == GameMode.Awd ? new AwdChallenge() : new KohChallenge();
            challenge.Id = challengeId; challenge.OwnerId = userId; challenge.Title = "Point"; challenge.Direction = "Web";
            challenge.Definition = TestConfigurations.Definition(mode); challenge.CreatedAt = now; challenge.UpdatedAt = now; db.Challenges.Add(challenge);
            CompetitionChallenge instance = mode == GameMode.Awd ? new AwdCompetitionChallenge() : new KohCompetitionChallenge();
            instance.Id = instanceId; instance.CompetitionId = competitionId; instance.ChallengeId = challengeId;
            instance.Rules = TestConfigurations.Rules(mode); instance.IsPublished = true; instance.UpdatedAt = now; db.CompetitionChallenges.Add(instance);
            foreach (var id in new[] { teamId, exhibitionId, defenderId })
            {
                var captain = Guid.NewGuid();
                db.Users.Add(new User { Id = captain, UserName = captain.ToString(), NormalizedUserName = captain.ToString().ToUpperInvariant(), Email = $"{captain}@example.test", PasswordHash = "test", Kind = UserKind.Human, Role = UserRole.User, CreatedAt = now, UpdatedAt = now });
                db.Teams.Add(new Team { Id = id, CompetitionId = competitionId, Name = id.ToString(), CaptainId = captain, MemberIds = [captain], InvitationToken = id.ToString("N"), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = now });
            }
            GameplayFact Fact(Guid? team, DateTimeOffset at, GameplayFactResult? result, GameplayFactState state = GameplayFactState.Completed)
            {
                GameplayFact fact = mode == GameMode.Awd ? new AwdServiceTransitionGameplayFact() : new KohControlObservationGameplayFact();
                fact.Id = Guid.CreateVersion7(); fact.CompetitionId = competitionId; fact.CompetitionChallengeId = instanceId;
                fact.TeamId = team; fact.OccurredAt = at; fact.UpdatedAt = at; fact.Result = result; fact.State = state;
                return fact;
            }
            db.GameplayFacts.AddRange(
                Fact(teamId, now.AddMinutes(-40), mode == GameMode.Awd ? GameplayFactResult.ServiceUp : GameplayFactResult.Controlled),
                Fact(mode == GameMode.Awd ? teamId : exhibitionId, now.AddMinutes(-2), mode == GameMode.Awd ? GameplayFactResult.ServiceDown : GameplayFactResult.Controlled),
                Fact(teamId, now.AddMinutes(-1), null, GameplayFactState.PlatformFailed),
                Fact(teamId, now.AddMinutes(1), mode == GameMode.Awd ? GameplayFactResult.ServiceUp : GameplayFactResult.Uncontrolled));
            if (mode == GameMode.Awd)
            {
                var round = Guid.NewGuid();
                FlagAttemptGameplayFact Attack(Guid source, Guid victim, int minute) => new()
                {
                    Id = Guid.CreateVersion7(), CompetitionId = competitionId, CompetitionChallengeId = instanceId,
                    TeamId = source, VictimTeamId = victim, ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = round,
                    OccurredAt = now.AddMinutes(minute), UpdatedAt = now, State = GameplayFactState.Completed, Result = GameplayFactResult.Correct
                };
                db.GameplayFacts.AddRange(Attack(teamId, defenderId, -3), Attack(teamId, defenderId, -2),
                    Attack(exhibitionId, defenderId, -2), Attack(teamId, teamId, -2));
                ((AwdCompetitionModeConfiguration)competition.ModeConfiguration).HardeningDurationSeconds = 600;
            }
            await db.SaveChangesAsync(ct);
            var input = new LeaderboardProjectionInput(competitionId, mode,
                [new(teamId, "Competitive", false, false), new(defenderId, "Non-scoring competitive", false, false, EarnsScore: false), new(exhibitionId, "Exhibition", false, false, AffectsCompetitiveResults: false)], [],
                [new(instanceId, "Web", "Point", false, instance.Rules, IsPublished: true)], competition.ModeConfiguration,
                now.AddHours(-1), LifecycleAudits: [
                    new() { Id = Guid.NewGuid(), OccurredAt = now.AddHours(-1), From = CompetitionStatus.Published, To = CompetitionStatus.Running },
                    new() { Id = Guid.NewGuid(), OccurredAt = now.AddMinutes(-55), From = CompetitionStatus.Running, To = CompetitionStatus.Paused },
                    new() { Id = Guid.NewGuid(), OccurredAt = now.AddMinutes(-2), From = CompetitionStatus.Paused, To = CompetitionStatus.Running }
                ], ProjectedAt: now, CompetitionStatus: CompetitionStatus.Running);
            var frame = (await SpectatorFrameReader.ReadAsync(db, "Broadcast", input, ct))!;
            if (mode == GameMode.Awd)
            {
                await Assert.That(frame.Services.Single(x => x.TeamId == teamId).Health).IsEqualTo(SpectatorHealth.Unknown);
                await Assert.That(frame.Services.Single(x => x.TeamId == exhibitionId).CheckedAt).IsNull();
                await Assert.That(frame.Changes.Count).IsEqualTo(1);
                await Assert.That(frame.Changes.Single().At).IsEqualTo(now.AddMinutes(-3));
                await Assert.That(frame.HardeningEndsAt).IsEqualTo(now.AddMinutes(3));
                var preview = await SpectatorFrameReader.ReadAsync(db, "Broadcast", input, ct,
                    new ChallengeTimingProjectionOverride(instanceId, new ChallengeTiming(now.AddMinutes(-1), null, null)));
                await Assert.That(preview!.Changes).IsEmpty();
            }
            else
            {
                await Assert.That(frame.Points.Single().ControllerId).IsEqualTo(teamId);
                await Assert.That(frame.Points.Single().Health).IsEqualTo(SpectatorHealth.Unknown);
                await Assert.That(frame.Changes.Count).IsEqualTo(2);
                await Assert.That(frame.Changes.All(x => x.ControllerId == teamId)).IsTrue();
                await Assert.That(frame.Changes.Single(x => x.Kind == SpectatorChangeKind.Control).TeamId).IsEqualTo(exhibitionId);
                db.GameplayFacts.Add(Fact(null, now.AddSeconds(-20), GameplayFactResult.Uncontrolled));
                await db.SaveChangesAsync(ct);
                var released = (await SpectatorFrameReader.ReadAsync(db, "Broadcast", input, ct))!;
                await Assert.That(released.Points.Single().ControllerId).IsNull();
                await Assert.That(released.Changes.First().Kind).IsEqualTo(SpectatorChangeKind.Uncontrolled);
            }
            await Assert.That(frame.Changes.All(x => x.At <= now)).IsTrue();

            // The public freeze uses the immutable persisted generation, including health
            // and ownership, even if a past fact is corrected after capture.
            competition.FrozenStartAt = now;
            await db.SaveChangesAsync(ct);
            using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
            var cache = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>(), clock: new FakeTimeProvider(now));
            await Assert.That(await cache.GetFrozenSpectatorScoreboardAsync(competitionId, ct)).IsNull();
            await cache.GetFrozenWebhookScoreboardAsync(competitionId, now, ct);
            var captured = await cache.GetFrozenSpectatorScoreboardAsync(competitionId, ct);
            await Assert.That(captured!.Spectator).IsNotNull();
            var before = System.Text.Json.JsonSerializer.Serialize(captured.Spectator);
            var failed = await db.GameplayFacts.SingleAsync(x => x.State == GameplayFactState.PlatformFailed, ct);
            failed.State = GameplayFactState.Completed;
            failed.Result = mode == GameMode.Awd ? GameplayFactResult.ServiceUp : GameplayFactResult.Uncontrolled;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            var after = await cache.GetFrozenSpectatorScoreboardAsync(competitionId, ct);
            await Assert.That(System.Text.Json.JsonSerializer.Serialize(after!.Spectator)).IsEqualTo(before);

            var access = new CompetitionVisibilityAccess(db);
            await Assert.That((await access.ResolveAsync(Guid.Empty, competitionId, now, ct))!.DataScope).IsEqualTo(LeaderboardDataScope.Frozen);
            await Assert.That((await access.ResolveAsync(userId, competitionId, now, ct))!.DataScope).IsEqualTo(LeaderboardDataScope.Live);
            var persisted = await db.Competitions.SingleAsync(x => x.Id == competitionId, ct);
            persisted.AccessMode = CompetitionAccessMode.StaffOnly;
            await db.SaveChangesAsync(ct);
            await Assert.That(await access.ResolveAsync(Guid.Empty, competitionId, now, ct)).IsNull();
            await Assert.That(await access.ResolveAsync(userId, competitionId, now, ct)).IsNotNull();
        });
    }
}
