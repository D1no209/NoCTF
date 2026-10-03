using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Directions;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Directions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Competitions.Directions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionDirectionPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Catalogs_preserve_existing_challenges_and_scope_assignments_to_their_competition(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("directions").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName))
            .UseSnakeCaseNamingConvention().Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        db.Users.Add(new User { Id = ownerId, UserName = "owner", Email = "owner@test.invalid", PasswordHash = "test",
            AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now });
        var competitionIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in competitionIds)
            db.Competitions.Add(new CtfCompetition { Id = id, OwnerId = ownerId, Title = "Directions", Status = CompetitionStatus.Draft,
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf), FlagDerivationSecret = new byte[32], CreatedAt = now, UpdatedAt = now });
        var templateIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in templateIds)
            db.Challenges.Add(new CtfChallenge { Id = id, OwnerId = ownerId, Title = "Web template", Direction = "Web",
                Definition = TestConfigurations.Definition(GameMode.Ctf), CreatedAt = now, UpdatedAt = now });
        var ccId = Guid.NewGuid();
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge { Id = ccId, CompetitionId = competitionIds[0], ChallengeId = templateIds[0],
            Rules = TestConfigurations.Rules(GameMode.Ctf), UpdatedAt = now, IsPublished = true });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        // Simulate a pre-upgrade competition with no direction catalog.
        await db.Set<CompetitionDirection>().Where(item => item.CompetitionId == competitionIds[0]).ExecuteDeleteAsync(ct);
        await CompetitionDirectionBaseline.InitializeAsync(db, ct);
        db.ChangeTracker.Clear();
        await CompetitionDirectionBaseline.InitializeAsync(db, ct);
        await Assert.That(await db.Set<CompetitionDirection>().CountAsync(ct)).IsEqualTo(28);
        db.ChangeTracker.Clear();
        var publisher = Substitute.For<IPostCommitMessagePublisher>();
        var catalog = new ManageCompetitionDirections(new CompetitionDirectionStore(db, Substitute.For<ICompetitionEventRecorder>(), publisher));
        var directions = (await catalog.ListAsync(competitionIds[0], ct))!;
        var web = directions.Single(item => item.Name == "Web");
        var changed = directions.Select(item => item.Id == web.Id ? item with { Name = "Web / 云原生", Icon = "server-cog" } : item).ToArray();
        await Assert.That((await catalog.SaveAsync(competitionIds[0], changed, now, ct)).Failure).IsNull();
        db.ChangeTracker.Clear();
        var challenges = new ChallengeManagementStore(db, publisher, Substitute.For<IChallengeRuntimeTemplateCatalog>());
        var detail = await challenges.FindAsync(competitionIds[0], ccId, true, false, ct);
        await Assert.That(detail!.Direction).IsEqualTo("Web / 云原生");
        await Assert.That(detail.DirectionIcon).IsEqualTo("server-cog");
        await Assert.That(detail.DirectionId).IsEqualTo(web.Id);
        await Assert.That((await db.Challenges.AsNoTracking().SingleAsync(item => item.Id == templateIds[0], ct)).Direction).IsEqualTo("Web");
        await Assert.That((await catalog.ListAsync(competitionIds[1], ct))!.Single(item => item.Name == "Web").Icon).IsEqualTo("globe");
        var imported = await challenges.CreateAsync(new(null, competitionIds[0], templateIds[1], 1, now), TestConfigurations.Rules(GameMode.Ctf), ct);
        await Assert.That(imported.Challenge!.DirectionId).IsEqualTo(web.Id);
        var foreign = (await catalog.ListAsync(competitionIds[1], ct))!.First();
        var invalid = await challenges.UpdateAsync(new(competitionIds[0], ccId, 0, true, now, DirectionId: foreign.Id), ct);
        await Assert.That(invalid.Failure).IsEqualTo(ChallengeMutationFailure.InvalidDirection);
        db.ChangeTracker.Clear();
        var custom = new CompetitionDirectionView(Guid.NewGuid(), "自定义 Research", "network");
        var withCustom = changed.Append(custom).ToArray();
        await Assert.That((await catalog.SaveAsync(competitionIds[0], withCustom, now, ct)).Failure).IsNull();
        db.ChangeTracker.Clear();
        await Assert.That((await challenges.UpdateAsync(new(competitionIds[0], ccId, 0, true, now, DirectionId: custom.Id), ct)).Challenge!.Direction).IsEqualTo(custom.Name);
        var summary = (await challenges.ListAsync(competitionIds[0], true, false, ct)).Single(item => item.Id == ccId);
        await Assert.That(summary.DirectionIcon).IsEqualTo("network");
        await Assert.That(summary.DirectionId).IsEqualTo(custom.Id);
        await challenges.SoftDeleteAsync(competitionIds[0], ccId, now, ct);
        db.ChangeTracker.Clear();
        await Assert.That((await catalog.SaveAsync(competitionIds[0], changed, now, ct)).Failure).IsEqualTo(CompetitionDirectionFailure.DirectionInUse);
        // Even direct persistence cannot attach another competition's direction.
        var entity = await db.CompetitionChallenges.IgnoreQueryFilters().SingleAsync(item => item.Id == ccId, ct);
        entity.Direction = null;
        entity.DirectionId = foreign.Id;
        var rejected = false;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
    });
}
