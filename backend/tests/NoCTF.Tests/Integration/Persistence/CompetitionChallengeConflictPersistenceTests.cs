using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionChallengeConflictPersistenceTests
{
    [Test, Timeout(300_000)]
    [Arguments(false)]
    [Arguments(true)]
    public Task Create_and_update_report_conflicts_with_active_or_deleted_instances(bool deleted, CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("challenge_conflicts").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var owner = Guid.NewGuid();
            var competitionId = Guid.NewGuid();
            var templates = new[] { Guid.NewGuid(), Guid.NewGuid() };
            db.Users.Add(new User { Id = owner, UserName = "owner", Email = "owner@test.invalid", PasswordHash = "test",
                AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now });
            db.Competitions.Add(new CtfCompetition { Id = competitionId, OwnerId = owner, Title = "Conflicts",
                Status = CompetitionStatus.Draft, ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                FlagDerivationSecret = new byte[32], CreatedAt = now, UpdatedAt = now });
            foreach (var templateId in templates)
                db.Challenges.Add(new CtfChallenge { Id = templateId, OwnerId = owner, Title = "Template", Direction = "Web",
                    Definition = TestConfigurations.Definition(GameMode.Ctf), CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(ct);
            var publisher = Substitute.For<IPostCommitMessagePublisher>();
            var store = new ChallengeManagementStore(db, publisher, Substitute.For<IChallengeRuntimeTemplateCatalog>());
            var reserved = await store.CreateAsync(new(null, competitionId, templates[0], 73, now),
                TestConfigurations.Rules(GameMode.Ctf), ct);
            await Assert.That(reserved.Failure).IsNull();
            if (deleted)
                await store.SoftDeleteAsync(competitionId, reserved.Challenge!.Id, now, ct);
            db.ChangeTracker.Clear();

            var orderConflict = await store.CreateAsync(new(null, competitionId, templates[1], 73, now),
                TestConfigurations.Rules(GameMode.Ctf), ct);
            await Assert.That(orderConflict.Failure).IsEqualTo(ChallengeMutationFailure.ChallengeOrderConflict);
            var templateConflict = await store.CreateAsync(new(null, competitionId, templates[0], 75, now),
                TestConfigurations.Rules(GameMode.Ctf), ct);
            await Assert.That(templateConflict.Failure).IsEqualTo(ChallengeMutationFailure.ChallengeTemplateConflict);
            db.ChangeTracker.Clear();
            await Assert.That(await db.CompetitionChallenges.IgnoreQueryFilters().CountAsync(ct)).IsEqualTo(1);

            var created = await store.CreateAsync(new(null, competitionId, templates[1], 75, now),
                TestConfigurations.Rules(GameMode.Ctf), ct);
            await Assert.That(created.Failure).IsNull();
            var id = created.Challenge!.Id;
            var update = await store.UpdateAsync(new(competitionId, id, 73, false, now, CustomTitle: "Rejected"), ct);
            await Assert.That(update.Failure).IsEqualTo(ChallengeMutationFailure.ChallengeOrderConflict);
            db.ChangeTracker.Clear();
            var unchanged = await db.CompetitionChallenges.SingleAsync(item => item.Id == id, ct);
            await Assert.That(unchanged.Order).IsEqualTo(75);
            await Assert.That(unchanged.CustomTitle).IsNull();
            if (deleted)
            {
                await Assert.That(await store.RestoreAsync(competitionId, reserved.Challenge!.Id, now, ct)).IsNull();
                db.ChangeTracker.Clear();
                await Assert.That(await db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(2);
            }
        });
}
