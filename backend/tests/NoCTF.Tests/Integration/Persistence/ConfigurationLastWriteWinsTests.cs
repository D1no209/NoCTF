using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Challenges.Configuration;
using NoCTF.Infrastructure.Competitions.Configuration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Competitions.Events;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ConfigurationLastWriteWinsTests
{
    [Test, Timeout(120_000)]
    public async Task Running_ctf_settlement_changes_persist_and_identical_or_rolled_back_saves_emit_no_event(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var ids = await SeedAsync(options, ct);
            await using (var db = new NoCtfDbContext(options))
                await db.Competitions.Where(competition => competition.Id == ids.CompetitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(competition => competition.Status, CompetitionStatus.Running), ct);
            var now = DateTimeOffset.UtcNow;
            await using (var db = new NoCtfDbContext(options))
            {
                var outbox = new NoOpPostCommitMessagePublisher();
                var store = new CompetitionConfigurationStore(db, outbox, new CompetitionEventStore(db, outbox));
                var current = (await store.FindAsync(ids.CompetitionId, ct))!;
                var configuration = (CtfCompetitionModeConfiguration)current.Configuration;
                configuration.ScoreSettlementMode = CtfScoreSettlementMode.AtSolve;
                configuration.DefaultScoreCurve.InitialPoints = 800;
                configuration.BloodRewards = [new() { Policy = CompetitionBloodRewardPolicy.CurrentPointsPercentage, Value = 10 }];
                await Assert.That((await store.TryUpdateAsync(ids.CompetitionId, configuration, true, now, ct)).Failure).IsNull();
                var persisted = (CtfCompetitionModeConfiguration)(await store.FindAsync(ids.CompetitionId, ct))!.Configuration;
                await Assert.That(persisted.ScoreSettlementMode).IsEqualTo(CtfScoreSettlementMode.AtSolve);
                await Assert.That(persisted.DefaultScoreCurve.InitialPoints).IsEqualTo(800);
                var count = await db.CompetitionEvents.CountAsync(ct);
                await store.TryUpdateAsync(ids.CompetitionId, persisted, true, now.AddSeconds(1), ct);
                await Assert.That(await db.CompetitionEvents.CountAsync(ct)).IsEqualTo(count);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var rollbackConfiguration = (CtfCompetitionModeConfiguration)(await store.FindAsync(ids.CompetitionId, ct))!.Configuration;
                rollbackConfiguration.ScoreSettlementMode = CtfScoreSettlementMode.DynamicRecalculation;
                await store.TryUpdateAsync(ids.CompetitionId, rollbackConfiguration, true, now.AddSeconds(2), ct);
                await transaction.RollbackAsync(ct);
            }
            await using (var db = new NoCtfDbContext(options))
            {
                var outbox = new NoOpPostCommitMessagePublisher();
                var store = new ChallengeConfigurationStore(db, outbox, new CompetitionEventStore(db, outbox));
                var rules = new CtfCompetitionChallengeRules { ScoreSettlementMode = CtfScoreSettlementMode.DynamicRecalculation };
                await Assert.That((await store.TryUpdateAsync(ids.CompetitionId, ids.CompetitionChallengeId, rules, now, ct)).Failure).IsNull();
                var before = await db.CompetitionEvents.CountAsync(ct);
                var current = (await store.FindAsync(ids.CompetitionId, ids.CompetitionChallengeId, ct))!;
                await Assert.That(((CtfCompetitionChallengeRules)current.Rules).ScoreSettlementMode).IsEqualTo(CtfScoreSettlementMode.DynamicRecalculation);
                await store.TryUpdateAsync(ids.CompetitionId, ids.CompetitionChallengeId, current.Rules, now.AddSeconds(1), ct);
                await Assert.That(await db.CompetitionEvents.CountAsync(ct)).IsEqualTo(before);
                ((CtfCompetitionChallengeRules)current.Rules).ScoreSettlementMode = null;
                await store.TryUpdateAsync(ids.CompetitionId, ids.CompetitionChallengeId, current.Rules, now.AddSeconds(2), ct);
            }
            await using var verification = new NoCtfDbContext(options);
            var competition = await verification.Competitions.AsNoTracking().SingleAsync(ct);
            var challenge = await verification.CompetitionChallenges.AsNoTracking().SingleAsync(ct);
            await Assert.That(((CtfCompetitionModeConfiguration)competition.ModeConfiguration!).ScoreSettlementMode).IsEqualTo(CtfScoreSettlementMode.AtSolve);
            await Assert.That(((CtfCompetitionChallengeRules)challenge.Rules!).ScoreSettlementMode).IsNull();
            await Assert.That(await verification.CompetitionEvents.CountAsync(ct)).IsEqualTo(3);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Cross_configuration_updates_apply_without_revision_fences(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var ids = await SeedAsync(options, cancellationToken);

            await using (var updateDb = new NoCtfDbContext(options))
            {
                var result = await new CompetitionConfigurationStore(
                    updateDb,
                    new NoOpPostCommitMessagePublisher(),
                    new CompetitionEventStore(updateDb, new NoOpPostCommitMessagePublisher())).TryUpdateAsync(
                    ids.CompetitionId,
                    new CtfCompetitionModeConfiguration
                    {
                        DefaultScoreCurve = new()
                        {
                            InitialPoints = 600,
                            MinimumPoints = 100,
                            DecayTeamCount = 10,
                            DecayMode = PersistedScoreDecayMode.Quadratic
                        }
                    },
                    true,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Configuration).IsNotNull();
            }

            await using (var updateDb = new NoCtfDbContext(options))
            {
                var result = await new ChallengeConfigurationStore(
                    updateDb,
                    new NoOpPostCommitMessagePublisher(),
                    new CompetitionEventStore(updateDb, new NoOpPostCommitMessagePublisher())).TryUpdateAsync(
                    ids.CompetitionId,
                    ids.CompetitionChallengeId,
                    new CtfCompetitionChallengeRules
                    {
                        HasScoreCurve = true,
                        ScoreCurve = new()
                        {
                            InitialPoints = 700,
                            MinimumPoints = 100,
                            DecayTeamCount = 10,
                            DecayMode = PersistedScoreDecayMode.Quadratic
                        }
                    },
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Configuration).IsNotNull();
            }

            await using var verifyDb = new NoCtfDbContext(options);
            var persisted = await verifyDb.CompetitionChallenges.AsNoTracking()
                .SingleAsync(challenge => challenge.Id == ids.CompetitionChallengeId, cancellationToken);
            await Assert.That(persisted.Rules!.ScoreCurve.InitialPoints).IsEqualTo(700);
            var persistedCompetition = await verifyDb.Competitions.AsNoTracking()
                .SingleAsync(competition => competition.Id == ids.CompetitionId, cancellationToken);
            await Assert.That(((CtfCompetitionModeConfiguration)persistedCompetition.ModeConfiguration!)
                .DefaultScoreCurve.InitialPoints).IsEqualTo(600);
        });
    }

    private static async Task<FixtureIds> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new CtfCompetition
        {
            Id = competitionId,
            Title = "Configuration fences",
            OwnerId = ownerId,
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Challenges.Add(new CtfChallenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "Challenge",
            Definition = TestConfigurations.Definition(GameMode.Ctf),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Rules = TestConfigurations.Rules(GameMode.Ctf),
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(competitionId, competitionChallengeId);
    }

    private sealed record FixtureIds(Guid CompetitionId, Guid CompetitionChallengeId);
}
