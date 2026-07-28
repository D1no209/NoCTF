using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Awd;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdRuntimeProvisioningTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Running_awd_competition_creates_and_dispatches_missing_team_runtimes_once(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_awd_runtime")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();

            await using var db = new NoCtfDbContext(options);
            var provisioner = new PostgresAwdRuntimeProvisioner(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                outbox,
                TimeProvider.System);
            var first = await provisioner.EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);
            var replay = await provisioner.EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);

            await Assert.That(first).IsEqualTo(AwdRuntimeProvisioningOutcome.Applied);
            await Assert.That(replay).IsEqualTo(AwdRuntimeProvisioningOutcome.Idempotent);
            var runtimes = await db.RuntimeInstances.AsNoTracking()
                .OrderBy(runtime => runtime.TeamId)
                .ToListAsync(cancellationToken);
            await Assert.That(runtimes.Count).IsEqualTo(2);
            await Assert.That(runtimes.Select(runtime => runtime.TeamId!.Value).ToHashSet()
                .SetEquals(fixture.ActiveTeamIds)).IsTrue();
            await Assert.That(runtimes.All(runtime => runtime.Generation == 1
                && runtime.State == RuntimeState.Queued
                && runtime.RuntimeProvider == RuntimeProvider.Docker
                && runtime.RuntimeKind == RuntimeKind.Container
                && runtime.RunnerPool == "awd-tests")).IsTrue();
            var dispatches = outbox.Published.OfType<DispatchRuntime>().ToArray();
            await Assert.That(dispatches.Length).IsEqualTo(2);
            await Assert.That(dispatches.Select(dispatch => dispatch.RuntimeInstanceId).ToHashSet()
                .SetEquals(runtimes.Select(runtime => runtime.Id))).IsTrue();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var blueUserId = Guid.CreateVersion7();
        var bannedUserId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var redTeamId = Guid.CreateVersion7();
        var blueTeamId = Guid.CreateVersion7();
        var bannedTeamId = Guid.CreateVersion7();
        db.Users.AddRange(
            User(ownerId, "awd-red", now),
            User(blueUserId, "awd-blue", now),
            User(bannedUserId, "awd-banned", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWD runtime provisioning",
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Status = CompetitionStatus.Running,
            ConfigurationJson = JsonSerializer.Serialize(
                AwdConfiguration.Default,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RunningSince = now,
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "AWD service",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            Revision = 3,
            ConfigurationJson = JsonSerializer.Serialize(
                new AwdChallengeConfiguration(
                    AwdChallengeConfiguration.CurrentSchemaVersion,
                    Runtime: new ChallengeRuntimeTemplate(
                        RuntimeProvider.Docker,
                        RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition("awd-runtime:fixture"),
                        new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                        RunnerPool: "awd-tests",
                        FlagSource: RuntimeFlagSource.AwdRotation),
                    FlagInjection: new AwdFlagInjectionConfiguration(
                        "printf '%s' '${FLAG}' > /dev/shm/flag")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = now
        });
        db.Teams.AddRange(
            Team(redTeamId, competitionId, ownerId, "Red", false, now),
            Team(blueTeamId, competitionId, blueUserId, "Blue", false, now),
            Team(bannedTeamId, competitionId, bannedUserId, "Banned", true, now));
        await db.SaveChangesAsync(cancellationToken);
        return new(competitionId, [redTeamId, blueTeamId]);
    }

    private static User User(Guid id, string name, DateTimeOffset now) => new()
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

    private static Team Team(
        Guid id,
        Guid competitionId,
        Guid captainId,
        string name,
        bool banned,
        DateTimeOffset now) => new()
    {
        Id = id,
        CompetitionId = competitionId,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        CaptainId = captainId,
        MemberIds = [captainId],
        InvitationToken = id.ToString("N"),
        RegistrationStatus = TeamRegistrationStatus.Approved,
        IsBanned = banned,
        RegisteredAt = now
    };

    private sealed record Fixture(Guid CompetitionId, IReadOnlyList<Guid> ActiveTeamIds);

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
