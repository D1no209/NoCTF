using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Hints;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Fixtures;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeHintUnlockPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Different_teams_unlocking_hints_increment_the_shared_revision_atomically(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_hint_unlock_revision")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var updateBarrier = new CompetitionLeaderboardUpdateBarrier();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(updateBarrier)
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();
            updateBarrier.Enable();

            var attempts = await Task.WhenAll(
                UnlockAsync(
                    options,
                    outbox,
                    fixture,
                    fixture.FirstMemberId,
                    fixture.Now,
                    cancellationToken),
                UnlockAsync(
                    options,
                    outbox,
                    fixture,
                    fixture.SecondMemberId,
                    fixture.Now.AddTicks(TimeSpan.TicksPerMicrosecond),
                    cancellationToken));

            await Assert.That(attempts.All(attempt =>
                    attempt.Failure is null && attempt.Result?.Created == true))
                .IsTrue();
            await Assert.That(updateBarrier.Arrivals).IsEqualTo(2);
            await using var verify = new NoCtfDbContext(options);
            var unlocks = await verify.ScoringEvents.AsNoTracking()
                .Where(scoringEvent => scoringEvent.Kind == NoCTF.Domain.Submissions.ScoringEventKind.HintUnlock)
                .ToListAsync(cancellationToken);
            await Assert.That(unlocks).Count().IsEqualTo(2);
            await Assert.That(unlocks.Select(scoringEvent => scoringEvent.TeamId).Distinct())
                .Count().IsEqualTo(2);
            var leaderboardRevision = await verify.Competitions.AsNoTracking()
                .Where(competition => competition.Id == fixture.CompetitionId)
                .Select(competition => competition.LeaderboardRevision)
                .SingleAsync(cancellationToken);
            await Assert.That(leaderboardRevision).IsEqualTo(2);
            await Assert.That(outbox.Published.OfType<ProjectLeaderboard>().Count())
                .IsEqualTo(2);
        });
    }

    private static async Task<HintUnlockAttempt> UnlockAsync(
        DbContextOptions<NoCtfDbContext> options,
        ITransactionalMessageOutbox outbox,
        Fixture fixture,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new ChallengeHintStore(db, new FixedScoreProjection(), outbox)
            .UnlockAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.HintId,
                userId,
                now,
                cancellationToken);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var ownerId = Guid.CreateVersion7(now);
        var firstMemberId = Guid.CreateVersion7(now);
        var secondMemberId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now);
        var challengeId = Guid.CreateVersion7(now);
        var competitionChallengeId = Guid.CreateVersion7(now);
        var hintId = Guid.CreateVersion7(now);
        db.Users.AddRange(
            NewUser(ownerId, "owner", now),
            NewUser(firstMemberId, "first", now),
            NewUser(secondMemberId, "second", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Concurrent hint unlocks",
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            ConfigurationJson = "{}",
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-1),
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = "Hinted challenge",
            Direction = "Web",
            DefinitionJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 100,
            Order = 1,
            IsPublished = true,
            RulesJson = "{}",
            UpdatedAt = now
        });
        db.Set<CompetitionChallengeHint>().Add(new CompetitionChallengeHint
        {
            Id = hintId,
            CompetitionChallengeId = competitionChallengeId,
            Content = "Concurrent hint",
            Cost = 10,
            PublishedAt = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.AddRange(
            NewTeam(
                Guid.CreateVersion7(now),
                competitionId,
                firstMemberId,
                "First",
                new string('a', 32),
                now),
            NewTeam(
                Guid.CreateVersion7(now),
                competitionId,
                secondMemberId,
                "Second",
                new string('b', 32),
                now));
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            competitionId,
            competitionChallengeId,
            hintId,
            firstMemberId,
            secondMemberId);
    }

    private static User NewUser(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Team NewTeam(
        Guid id,
        Guid competitionId,
        Guid memberId,
        string name,
        string invitationToken,
        DateTimeOffset now) => new()
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CaptainId = memberId,
            MemberIds = [memberId],
            InvitationToken = invitationToken,
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        };

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid HintId,
        Guid FirstMemberId,
        Guid SecondMemberId);

    private sealed class FixedScoreProjection : ILeaderboardProjectionEngine
    {
        public LeaderboardProjectionResult Project(LeaderboardProjectionInput input) =>
            new(
                input.Teams.Select((team, index) => new LeaderboardEntry(
                    index + 1,
                    team.Id,
                    team.Name,
                    100,
                    1,
                    input.ProjectedAt,
                    [])).ToArray(),
                [],
                []);
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ConcurrentQueue<object> Published { get; } = new();

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
