using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class LeaderboardProjectionPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Koh_schedule_aggregates_history_in_PostgreSQL_and_uses_the_latest_observation(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var commands = new CommandCaptureInterceptor();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().AddInterceptors(commands).Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Koh, 0, owner.Id, now);
            ((KohCompetitionChallengeRules)fixture.CompetitionChallenge.Rules!).PollIntervalSeconds = 10;
            fixture.Facts[0].OccurredAt = now.AddSeconds(-2);
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.AddRange(fixture.Facts);
            db.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent
            {
                Id = Guid.CreateVersion7(fixture.Competition.StartAt), CompetitionId = fixture.Competition.Id,
                SubjectType = EntityReferenceKind.Competition, SubjectId = fixture.Competition.Id,
                PreviousCompetitionStatus = CompetitionStatus.Published, CompetitionStatus = CompetitionStatus.Running,
                OccurredAt = fixture.Competition.StartAt
            });
            await db.SaveChangesAsync(ct);
            commands.Clear();
            var source = new ClusterScheduleSource(db, new AwdRoundConfigurationCatalog(), new KohProducerConfigurationCatalog(),
                NullLogger<ClusterScheduleSource>.Instance, []);

            var schedule = await source.RebuildAsync(now, ct);

            var poll = schedule.Single(item => item.Kind == ClusterScheduleKind.KohPoll);
            await Assert.That(poll.DueAt).IsEqualTo(now.AddSeconds(8));
            var observations = commands.Commands.Single(command => command.Contains("gameplay_facts", StringComparison.Ordinal)
                && command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
            await Assert.That(observations).Contains("max(");
        });
    }
}
