using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class IdentityNormalizationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Usernames_and_team_names_are_trimmed_and_case_insensitive(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_identity_normalization")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
            {
                IterationCount = 10_000
            }));
            var now = DateTimeOffset.UtcNow;
            Guid firstUserId;
            Guid secondUserId;

            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.MigrateAsync(cancellationToken);
                var store = new AuthenticationStore(db, hasher);
                var first = await new RegisterUser(store).ExecuteAsync(
                    new("  Player_One  ", "player-one@example.test", "eight888", now),
                    cancellationToken);
                var second = await new RegisterUser(store).ExecuteAsync(
                    new("Player_Two", "player-two@example.test", "eight888", now.AddTicks(1)),
                    cancellationToken);

                await Assert.That(first.Succeeded).IsTrue();
                await Assert.That(first.Value!.Profile.UserName).IsEqualTo("Player_One");
                await Assert.That(second.Succeeded).IsTrue();
                firstUserId = first.Value.Profile.Id;
                secondUserId = second.Value!.Profile.Id;

                db.Competitions.Add(new Competition
                {
                    Id = Guid.CreateVersion7(),
                    OwnerId = firstUserId,
                    Title = "Normalization",
                    Mode = GameMode.Ctf,
                    Status = CompetitionStatus.Draft,
                    ConfigurationJson = "{}",
                    MaxTeamMembers = 5,
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddHours(1),
                    EndAt = now.AddHours(2),
                    CreatedAt = now,
                    UpdatedAt = now,
                    ConfigurationUpdatedAt = now
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var store = new AuthenticationStore(db, hasher);
                var duplicate = await new RegisterUser(store).ExecuteAsync(
                    new("player_one", "different@example.test", "eight888", now.AddTicks(2)),
                    cancellationToken);
                var login = await store.FindByLoginAsync("  pLaYeR_oNe  ", cancellationToken);

                await Assert.That(duplicate.FailureCode).IsEqualTo(RegisterUserFailureCode.UserNameConflict);
                await Assert.That(login!.Id).IsEqualTo(firstUserId);
            }

            Guid competitionId;
            await using (var db = new NoCtfDbContext(options))
            {
                competitionId = await db.Competitions.Select(competition => competition.Id)
                    .SingleAsync(cancellationToken);
                var store = new TeamRegistrationStore(db, new NoopOutbox());
                var created = await new CreateTeam(store).ExecuteAsync(
                    new(competitionId, firstUserId, "  Alpha  ", now, "default"),
                    cancellationToken);

                await Assert.That(created.Succeeded).IsTrue();
                await Assert.That(created.Value!.Name).IsEqualTo("Alpha");
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var store = new TeamRegistrationStore(db, new NoopOutbox());
                var duplicate = await new CreateTeam(store).ExecuteAsync(
                    new(competitionId, secondUserId, "alpha", now.AddTicks(1), "default"),
                    cancellationToken);

                await Assert.That(duplicate.FailureCode)
                    .IsEqualTo(TeamRegistrationFailure.TeamNameOrMembershipConflict);
            }
        });
    }

    private sealed class NoopOutbox : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
