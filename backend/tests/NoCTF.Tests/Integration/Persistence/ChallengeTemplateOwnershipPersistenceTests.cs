using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTemplateOwnershipPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Only_mine_filters_owners_before_pagination_and_combines_with_other_filters(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_challenge_ownership")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7();
            var otherId = Guid.CreateVersion7();
            db.Users.AddRange(User(ownerId, "owner", now), User(otherId, "other", now));
            var alpha = Template(ownerId, "Own alpha", "Web", now);
            var beta = Template(ownerId, "Own beta", "Pwn", now.AddMinutes(1));
            beta.Visibility = ChallengeVisibility.Shared;
            var deleted = Template(ownerId, "Own deleted", "Web", now.AddMinutes(2));
            deleted.DeletedAt = now;
            var shared = Template(otherId, "Shared", "Crypto", now.AddMinutes(3));
            shared.Visibility = ChallengeVisibility.Shared;
            var managed = Template(otherId, "Managed", "Reverse", now.AddMinutes(4));
            managed.ManagerIds = [ownerId];
            var hidden = Template(otherId, "Private", "Misc", now.AddMinutes(5));
            db.Challenges.AddRange(alpha, beta, deleted, shared, managed, hidden);
            await db.SaveChangesAsync(cancellationToken);
            var store = new ChallengeBankStore(db);

            var all = await store.ListPageAsync(
                new(ownerId, false, false, null, null, 0, 10, false), cancellationToken);
            await Assert.That(all.Total).IsEqualTo(4);
            var administrator = await store.ListPageAsync(
                new(ownerId, true, false, null, null, 0, 10, false), cancellationToken);
            await Assert.That(administrator.Total).IsEqualTo(5);

            foreach (var isAdministrator in new[] { false, true })
            {
                var mine = await store.ListPageAsync(
                    new(ownerId, isAdministrator, false, null, null, 1, 1, false, OnlyMine: true),
                    cancellationToken);
                await Assert.That(mine.Total).IsEqualTo(2);
                await Assert.That(mine.Items.Single().Id).IsEqualTo(beta.Id);
                await Assert.That(mine.Directions.SequenceEqual(new[] { "Pwn", "Web" })).IsTrue();
            }

            var searched = await store.ListPageAsync(
                new(ownerId, true, false, "alpha", "Web", 0, 10, true, OnlyMine: true),
                cancellationToken);
            await Assert.That(searched.Total).IsEqualTo(1);
            await Assert.That(searched.Items.Single().Id).IsEqualTo(alpha.Id);
            var deletedOnly = await store.ListPageAsync(
                new(ownerId, true, true, "deleted", "Web", 0, 10, false, OnlyMine: true),
                cancellationToken);
            await Assert.That(deletedOnly.Total).IsEqualTo(1);
            await Assert.That(deletedOnly.Items.Single().Id).IsEqualTo(deleted.Id);
            var other = await store.ListPageAsync(
                new(otherId, false, false, null, null, 0, 10, false, OnlyMine: true),
                cancellationToken);
            await Assert.That(other.Total).IsEqualTo(3);
            await Assert.That(other.Items.Any(item => item.Id == alpha.Id || item.Id == beta.Id)).IsFalse();
            var empty = await store.ListPageAsync(
                new(Guid.CreateVersion7(), true, false, null, null, 0, 10, false, OnlyMine: true),
                cancellationToken);
            await Assert.That(empty.Total).IsEqualTo(0);
            await Assert.That(empty.Items.Count).IsEqualTo(0);
            await Assert.That(empty.Directions.Count).IsEqualTo(0);
        });
    }

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        Kind = UserKind.Human,
        Role = UserRole.Organizer,
        EmailVerifiedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static CtfChallenge Template(Guid ownerId, string title, string direction, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        OwnerId = ownerId,
        Title = title,
        Direction = direction,
        Visibility = ChallengeVisibility.Private,
        Definition = TestConfigurations.Definition(GameMode.Ctf),
        CreatedAt = now,
        UpdatedAt = now
    };
}
