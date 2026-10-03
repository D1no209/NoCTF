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
public sealed class CompetitionChallengeTagPersistenceTests
{
    [Test, Timeout(300_000)]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    [Arguments(GameMode.Koh)]
    public Task Tags_are_scoped_ordered_and_preserved_by_other_updates(GameMode mode, CancellationToken ct) =>
        WithDatabaseAsync(mode, async (options, competitions, templates) =>
        {
            await using var db = new NoCtfDbContext(options);
            var store = Store(db);
            var first = await store.CreateAsync(new(null, competitions[0], templates[0], 1, DateTimeOffset.UtcNow,
                Tags: ["Web", "SQL"]), TestConfigurations.Rules(mode), ct);
            var other = await store.CreateAsync(new(null, competitions[1], templates[0], 1, DateTimeOffset.UtcNow,
                Tags: ["Research"]), TestConfigurations.Rules(mode), ct);
            var hidden = await store.CreateAsync(new(null, competitions[0], templates[1], 2, DateTimeOffset.UtcNow,
                Tags: ["Secret"]), TestConfigurations.Rules(mode), ct);
            var id = first.Challenge!.Id;
            db.ChangeTracker.Clear();
            var update = new UpdateChallenge(store);
            await update.ExecuteAsync(new(competitions[0], id, 1, true, DateTimeOffset.UtcNow), ct);
            db.ChangeTracker.Clear();
            var detail = await store.FindAsync(competitions[0], id, false, false, ct);
            var list = await store.ListAsync(competitions[0], false, false, ct);
            await Assert.That(detail!.Tags.SequenceEqual(["Web", "SQL"])).IsTrue();
            await Assert.That(list.Count).IsEqualTo(1);
            await Assert.That(list[0].Tags.SequenceEqual(detail.Tags)).IsTrue();
            await Assert.That((await store.FindAsync(competitions[1], other.Challenge!.Id, true, false, ct))!
                .Tags.SequenceEqual(["Research"])).IsTrue();

            var stamp = await db.CompetitionChallenges.Where(item => item.Id == id).Select(item => item.ConcurrencyStamp).SingleAsync(ct);
            await update.ExecuteAsync(new(competitions[0], id, 1, true, DateTimeOffset.UtcNow,
                Tags: [" sql ", "SQL", "Crypto"]), ct);
            db.ChangeTracker.Clear();
            detail = await store.FindAsync(competitions[0], id, false, false, ct);
            await Assert.That(detail!.Tags.SequenceEqual(["sql", "Crypto"])).IsTrue();
            await Assert.That(await db.CompetitionChallenges.Where(item => item.Id == id)
                .Select(item => item.ConcurrencyStamp).SingleAsync(ct)).IsNotEqualTo(stamp);
            await update.ExecuteAsync(new(competitions[0], id, 1, true, DateTimeOffset.UtcNow, Tags: []), ct);
            db.ChangeTracker.Clear();
            await Assert.That((await store.FindAsync(competitions[0], id, false, false, ct))!.Tags.Count).IsEqualTo(0);

            await store.SoftDeleteAsync(competitions[0], hidden.Challenge!.Id, DateTimeOffset.UtcNow, ct);
            db.ChangeTracker.Clear();
            await Assert.That((await store.ListAsync(competitions[0], true, false, ct)).Count).IsEqualTo(1);
            await Assert.That((await store.ListAsync(competitions[0], true, true, ct)).Count).IsEqualTo(2);
        }, ct);

    [Test, Timeout(300_000)]
    public Task Unique_constraint_and_parent_concurrency_protect_the_tag_collection(CancellationToken ct) =>
        WithDatabaseAsync(GameMode.Ctf, async (options, competitions, templates) =>
        {
            Guid id;
            await using (var seed = new NoCtfDbContext(options))
            {
                id = (await Store(seed).CreateAsync(new(null, competitions[0], templates[0], 1, DateTimeOffset.UtcNow,
                    Tags: ["Web"]), TestConfigurations.Rules(GameMode.Ctf), ct)).Challenge!.Id;
            }
            await using var first = new NoCtfDbContext(options);
            await using var second = new NoCtfDbContext(options);
            var left = await first.CompetitionChallenges.SingleAsync(item => item.Id == id, ct);
            var right = await second.CompetitionChallenges.SingleAsync(item => item.Id == id, ct);
            left.Tags[0].Name = "WEB";
            left.UpdatedAt = DateTimeOffset.UtcNow;
            await first.SaveChangesAsync(ct);
            right.Tags[0].Name = "web";
            right.UpdatedAt = DateTimeOffset.UtcNow;
            await Assert.That(async () => await second.SaveChangesAsync(ct)).Throws<DbUpdateConcurrencyException>();
            second.ChangeTracker.Clear();
            right = await second.CompetitionChallenges.SingleAsync(item => item.Id == id, ct);
            right.Tags.Add(new() { Name = "web", NormalizedName = "WEB", Position = 1 });
            await Assert.That(async () => await second.SaveChangesAsync(ct)).Throws<DbUpdateException>();
        }, ct);

    [Test, Timeout(300_000)]
    public Task Transaction_rollback_restores_tags_and_presentation_together(CancellationToken ct) =>
        WithDatabaseAsync(GameMode.Ctf, async (options, competitions, templates) =>
        {
            await using var db = new NoCtfDbContext(options);
            var store = Store(db);
            var id = (await store.CreateAsync(new(null, competitions[0], templates[0], 1, DateTimeOffset.UtcNow,
                Tags: ["Original"]), TestConfigurations.Rules(GameMode.Ctf), ct)).Challenge!.Id;
            db.ChangeTracker.Clear();
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await new UpdateChallenge(store).ExecuteAsync(new(competitions[0], id, 1, true, DateTimeOffset.UtcNow,
                    CustomTitle: "Changed", Tags: ["Replacement"]), ct);
                await transaction.RollbackAsync(ct);
            }
            db.ChangeTracker.Clear();
            var detail = await store.FindAsync(competitions[0], id, true, false, ct);
            await Assert.That(detail!.Tags.SequenceEqual(["Original"])).IsTrue();
            await Assert.That(detail.CustomTitle).IsNull();
            await Assert.That(detail.IsPublished).IsFalse();
        }, ct);

    private static ChallengeManagementStore Store(NoCtfDbContext db) => new(
        db, Substitute.For<IPostCommitMessagePublisher>(), Substitute.For<IChallengeRuntimeTemplateCatalog>());

    private static Task WithDatabaseAsync(GameMode mode,
        Func<DbContextOptions<NoCtfDbContext>, Guid[], Guid[], Task> test, CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("tags").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention().Options;
            var competitions = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var templates = new[] { Guid.NewGuid(), Guid.NewGuid() };
            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.MigrateAsync(ct);
                var now = DateTimeOffset.UtcNow;
                var owner = Guid.NewGuid();
                db.Users.Add(new User { Id = owner, UserName = "owner", Email = "owner@test.invalid", PasswordHash = "test",
                    AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now });
                foreach (var id in competitions)
                {
                    var competition = CompetitionGeneratedCatalog.Create(mode);
                    competition.Id = id;
                    competition.OwnerId = owner;
                    competition.Title = "Tags";
                    competition.Status = CompetitionStatus.Draft;
                    competition.ModeConfiguration = TestConfigurations.Competition(mode);
                    competition.FlagDerivationSecret = new byte[32];
                    competition.CreatedAt = competition.UpdatedAt = now;
                    db.Competitions.Add(competition);
                }
                foreach (var id in templates)
                {
                    var template = ChallengeGeneratedCatalog.Create(mode);
                    template.Id = id;
                    template.OwnerId = owner;
                    template.Title = "Template";
                    template.Direction = "Web";
                    template.Definition = TestConfigurations.Definition(mode);
                    template.CreatedAt = template.UpdatedAt = now;
                    db.Challenges.Add(template);
                }
                await db.SaveChangesAsync(ct);
            }
            await test(options, competitions, templates);
        });
}
