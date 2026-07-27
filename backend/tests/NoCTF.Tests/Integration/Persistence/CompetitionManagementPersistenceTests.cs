using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionManagementPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Find_filters_the_entity_before_projecting_the_competition_view(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_competition_management")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();

            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = ownerId,
                UserName = "administrator",
                NormalizedUserName = "ADMINISTRATOR",
                Email = "administrator@example.test",
                NormalizedEmail = "ADMINISTRATOR@EXAMPLE.TEST",
                PasswordHash = "test",
                Role = UserRole.Administrator,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = ownerId,
                Title = "Competition persistence",
                Mode = GameMode.Ctf,
                StartAt = now,
                EndAt = now.AddHours(1),
                Status = CompetitionStatus.Published,
                MaxTeamMembers = 5,
                ConfigurationJson = "{}",
                FlagDerivationSecret = new byte[32],
                CreatedAt = now,
                UpdatedAt = now,
                ConfigurationUpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);

            var result = await new EfCompetitionManagementStore(db)
                .FindAsync(competitionId, includeDraft: false, cancellationToken);

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Id).IsEqualTo(competitionId);
        });
    }
}
