using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using NoCTF.Infrastructure.Competitions.Configuration;
using NoCTF.Infrastructure.Challenges.Configuration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Infrastructure.Messaging;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Administration.Challenges;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdRuleFoundationPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task MigrationPreservesAwdpAndRoundPoliciesUseRealRelationalConstraints(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), provider => provider.MigrationsAssembly("NoCTF.Persistence.PostgreSql"))
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            var migrations = db.Database.GetMigrations().ToArray();
            var index = Array.FindIndex(migrations, name => name.EndsWith("_AwdRuleFoundations", StringComparison.Ordinal));
            await Assert.That(index).IsGreaterThan(0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1], ct);
            var now = DateTimeOffset.UtcNow;
            var user = new User { Id = Guid.NewGuid(), UserName = "awd-foundation", Email = "awd-foundation@example.test", PasswordHash = "unused", CreatedAt = now };
            db.Users.Add(user);
            var awdp = new AwdpCompetition
            {
                Id = Guid.NewGuid(), OwnerId = user.Id, Title = "Preserved AWDP", StartAt = now, EndAt = now.AddHours(1),
                CreatedAt = now, UpdatedAt = now
            };
            awdp.ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Awdp, awdp.Id);
            ((AwdpCompetitionModeConfiguration)awdp.ModeConfiguration).ServiceAbnormalPenalty = 137;
            db.Competitions.Add(awdp);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            await db.Database.MigrateAsync(ct);
            var preserved = await db.Competitions.AsSplitQuery().SingleAsync(competition => competition.Id == awdp.Id, ct);
            await Assert.That(((AwdpCompetitionModeConfiguration)preserved.ModeConfiguration!).ServiceAbnormalPenalty).IsEqualTo(137L);

            var awd = new AwdCompetition
            {
                Id = Guid.NewGuid(), OwnerId = user.Id, Title = "AWD", StartAt = now, EndAt = now.AddHours(1), CreatedAt = now, UpdatedAt = now
            };
            awd.ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Awd, awd.Id);
            var template = new AwdChallenge
            {
                Id = Guid.NewGuid(), OwnerId = user.Id, Title = "Service", CreatedAt = now, UpdatedAt = now
            };
            var storeId = Guid.NewGuid();
            template.Definition = new AwdChallengeDefinition
            {
                FlagStores = [new() { Id = storeId, Name = "orders", Position = 0 }]
            };
            var challenge = new AwdCompetitionChallenge
            {
                Id = Guid.NewGuid(), CompetitionId = awd.Id, ChallengeId = template.Id,
                Rules = new AwdCompetitionChallengeRules
                {
                    AwdOverrides = new() { MumbleRatio = 0, RecoveringRatio = .75m },
                    StoreRules = [new() { StoreId = storeId, Weight = 3.25m }]
                },
                UpdatedAt = now
            };
            db.Competitions.Add(awd); db.Challenges.Add(template); db.CompetitionChallenges.Add(challenge);
            var round = new AwdRound
            {
                Id = Guid.NewGuid(), CompetitionId = awd.Id, Number = 1, StartsAt = now, EndsAt = now.AddMinutes(3),
                ActiveEndTicks = TimeSpan.FromMinutes(3).Ticks
            };
            db.AwdRounds.Add(round);
            var rules = new AwdRoundRules
            {
                Id = Guid.NewGuid(), CompetitionId = awd.Id, RoundId = round.Id, CompetitionChallengeId = challenge.Id,
                Options = new() { RecoveringRatio = .75m },
                Stores = [new() { StoreId = storeId, Weight = 3.25m, FlagsPerIssue = 2 }]
            };
            db.AwdRoundRules.Add(rules);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            var loaded = await db.AwdRoundRules.AsSplitQuery().SingleAsync(ct);
            await Assert.That(loaded.Options.FlagLifetime).IsEqualTo(5);
            await Assert.That(loaded.Options.RecoveringRatio).IsEqualTo(.75m);
            await Assert.That(loaded.Stores.Single().FlagsPerIssue).IsEqualTo(2);
            var loadedChallenge = await db.CompetitionChallenges.AsSplitQuery().SingleAsync(ct);
            await Assert.That(((AwdCompetitionChallengeRules)loadedChallenge.Rules!).AwdOverrides.MumbleRatio).IsEqualTo(0m);
            var modeStore = new CompetitionConfigurationStore(db, new NoOpPostCommitMessagePublisher(), NullCompetitionEventRecorder.Instance);
            var requested = (AwdCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awd, awd.Id);
            requested.AwdOptions = new() { Preset = AwdScoringPreset.Custom, FlagLifetime = 9,
                RecoveringRatio = .125m, MumblePenalty = 0, RetryCount = 0, Composition = AwdScoreComposition.Weighted,
                AttackWeight = .75m, TotalRounds = 123, SubmissionTailSeconds = 45 };
            var contract = CompetitionModeConfigurationContractMapper.FromDomain(requested);
            var saved = await modeStore.TryUpdateAsync(awd.Id,
                CompetitionModeConfigurationContractMapper.ToDomain(awd.Id, GameMode.Awd, contract), true, now.AddSeconds(1), ct);
            await Assert.That(saved.Failure).IsNull();
            db.ChangeTracker.Clear();
            var reloaded = await modeStore.FindAsync(awd.Id, ct);
            await Assert.That(((AwdCompetitionModeConfiguration)reloaded!.Configuration).AwdOptions).IsEqualTo(requested.AwdOptions);
            var challengeStore = new ChallengeConfigurationStore(db, new NoOpPostCommitMessagePublisher(), NullCompetitionEventRecorder.Instance);
            var requestedRules = new AwdCompetitionChallengeRules
            {
                AwdOverrides = new() { RecoveringRatio = 0m, RetryCount = 0, AttackSettlement = AwdAttackSettlement.RoundEnd },
                StoreRules = [new() { StoreId = storeId, Weight = .25m, FlagsPerIssue = 3 }]
            };
            var ruleContract = CompetitionChallengeRulesContractMapper.FromDomain(requestedRules);
            var savedRules = await challengeStore.TryUpdateAsync(awd.Id, challenge.Id,
                CompetitionChallengeRulesContractMapper.ToDomain(challenge.Id, GameMode.Awd, ruleContract), now.AddSeconds(2), ct);
            await Assert.That(savedRules.Failure).IsNull();
            db.ChangeTracker.Clear();
            var reloadedRules = (AwdCompetitionChallengeRules)(await challengeStore.FindAsync(awd.Id, challenge.Id, ct))!.Rules;
            await Assert.That(reloadedRules.AwdOverrides.RecoveringRatio).IsEqualTo(0m);
            await Assert.That(reloadedRules.AwdOverrides.FlagLifetime).IsNull();
            await Assert.That(reloadedRules.StoreRules.Single().FlagsPerIssue).IsEqualTo(3);
            await Assert.That(reloadedRules.StoreRules.Single().Weight).IsEqualTo(.25m);
            var removedRules = await challengeStore.TryUpdateAsync(awd.Id, challenge.Id,
                new AwdCompetitionChallengeRules(), now.AddSeconds(3), ct);
            await Assert.That(removedRules.Failure).IsNull();
            db.ChangeTracker.Clear();
            await Assert.That(((AwdCompetitionChallengeRules)(await challengeStore.FindAsync(awd.Id, challenge.Id, ct))!.Rules).StoreRules.Count).IsEqualTo(0);
            loaded = await db.AwdRoundRules.AsSplitQuery().SingleAsync(ct);
            await Assert.That(loaded.Options.RecoveringRatio).IsEqualTo(.75m);
            await Assert.That(loaded.Stores.Single().Weight).IsEqualTo(3.25m);
            await using var duplicate = new NoCtfDbContext(options);
            duplicate.AwdRounds.Add(new()
            {
                Id = Guid.NewGuid(), CompetitionId = awd.Id, Number = 1, StartsAt = now, EndsAt = now.AddMinutes(3)
            });
            await Assert.That(async () => await duplicate.SaveChangesAsync(ct)).Throws<DbUpdateException>();
            await using (var invalid = new NoCtfDbContext(options))
            {
                invalid.AwdRoundRules.Add(new()
                {
                    Id = Guid.NewGuid(), CompetitionId = awdp.Id, RoundId = round.Id,
                    CompetitionChallengeId = challenge.Id, Revision = 1, Options = new()
                });
                await Assert.That(async () => await invalid.SaveChangesAsync(ct)).Throws<DbUpdateException>();
            }
            loaded.Options = loaded.Options with { RecoveringRatio = .25m };
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }
}
