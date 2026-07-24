using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Messages;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class KohPollingPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Observation_writes_one_fact_and_schedules_only_the_next_future_poll(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_koh_poll")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var clock = new MutableTimeProvider(fixture.DueAt.AddSeconds(16));
            var configurations = new KohProducerConfigurationCatalog();
            RecordKohObservation observation;
            var dispatchOutbox = new RecordingOutbox();
            await using (var dispatchDb = new NoCtfDbContext(options))
            {
                await BackendMessageHandlers.Handle(
                    new DispatchRuntime(fixture.RuntimeId, 0),
                    dispatchDb,
                    new ChallengeRuntimeTemplateCatalog(),
                    dispatchOutbox,
                    cancellationToken);
            }
            var claim = dispatchOutbox.RunnerPoolMessages
                .OfType<ClaimContainerRuntime>()
                .Single();
            await Assert.That(claim.Definition.UrlBindings).Count().IsEqualTo(1);
            await Assert.That(claim.Definition.ControlCheckUrlBinding).IsNotNull();
            await Assert.That(claim.Definition.Labels["noctf.io/managed"])
                .IsEqualTo("true");
            await Assert.That(claim.Definition.Labels["noctf.io/runtime-instance-id"])
                .IsEqualTo(fixture.RuntimeId.ToString("D"));
            await Assert.That(claim.Definition.Labels["noctf.io/competition-id"])
                .IsEqualTo(fixture.CompetitionId.ToString("D"));
            await Assert.That(claim.Definition.Labels["noctf.io/competition-challenge-id"])
                .IsEqualTo(fixture.CompetitionChallengeId.ToString("D"));
            await Assert.That(claim.Definition.Labels["noctf.io/generation"])
                .IsEqualTo("1");
            await Assert.That(claim.Definition.Labels)
                .DoesNotContainKey("noctf.io/team-id");
            await using (var provisionDb = new NoCtfDbContext(options))
            {
                var runtime = await provisionDb.RuntimeInstances.SingleAsync(cancellationToken);
                runtime.State = RuntimeState.Provisioning;
                await provisionDb.SaveChangesAsync(cancellationToken);
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisioned(
                        fixture.RuntimeId,
                        0,
                        "runner-a",
                        RuntimeProvider.Docker,
                        "{}",
                        ["http://runner.example:32000/play"],
                        [0],
                        null,
                        "http://hill.internal/control"),
                    provisionDb,
                    new RecordingOutbox(),
                    cancellationToken);
            }

            await using (var readDb = new NoCtfDbContext(options))
            {
                var polling = new KohPollingHandler(
                    readDb,
                    new FixedKohControlClient(fixture.Flag),
                    configurations,
                    clock);
                observation = (await polling.Handle(
                    new PollKohChallenge(
                        fixture.CompetitionId,
                        fixture.CompetitionChallengeId,
                        0,
                        0,
                        fixture.DueAt,
                        fixture.DueAt),
                    cancellationToken))!;
            }

            await Assert.That(observation.Result).IsEqualTo(ScoringResult.Correct);
            await Assert.That(observation.TeamId).IsEqualTo(fixture.TeamId);
            var outbox = new RecordingOutbox();
            await using (var writeDb = new NoCtfDbContext(options))
            {
                var writer = new KohObservationHandler(
                    writeDb,
                    outbox,
                    configurations,
                    clock);
                await writer.Handle(observation, cancellationToken);
            }

            await using (var verify = new NoCtfDbContext(options))
            {
                var fact = await verify.ScoringEvents.AsNoTracking()
                    .SingleAsync(cancellationToken);
                await Assert.That(fact.Kind).IsEqualTo(ScoringEventKind.KohObservation);
                await Assert.That(fact.Result).IsEqualTo(ScoringResult.Correct);
                await Assert.That(fact.TeamId).IsEqualTo(fixture.TeamId);
                await Assert.That(
                    await verify.Competitions.Select(item => item.LeaderboardRevision)
                        .SingleAsync(cancellationToken))
                    .IsEqualTo(1);
            }

            var next = outbox.Scheduled.Single();
            await Assert.That(next.At).IsEqualTo(fixture.DueAt.AddSeconds(20));
            await Assert.That(next.Message).IsTypeOf<PollKohChallenge>();

            await using (var changeDb = new NoCtfDbContext(options))
            {
                await changeDb.Competitions.ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.ConfigurationRevision, 1),
                    cancellationToken);
            }
            var staleOutbox = new RecordingOutbox();
            await using (var staleDb = new NoCtfDbContext(options))
            {
                var writer = new KohObservationHandler(
                    staleDb,
                    staleOutbox,
                    configurations,
                    clock);
                await writer.Handle(observation, cancellationToken);
                await Assert.That(await staleDb.ScoringEvents.CountAsync(cancellationToken))
                    .IsEqualTo(1);
            }
            var replacement = (PollKohChallenge)staleOutbox.Scheduled.Single().Message;
            await Assert.That(replacement.CompetitionConfigurationRevision).IsEqualTo(1);

            await using (var resumeDb = new NoCtfDbContext(options))
            {
                await resumeDb.Competitions.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.RunningSince,
                        fixture.DueAt.AddMinutes(1)),
                    cancellationToken);
            }
            await using (var staleEpochDb = new NoCtfDbContext(options))
            {
                var staleEpoch = await new KohPollingHandler(
                    staleEpochDb,
                    new FixedKohControlClient(fixture.Flag),
                    configurations,
                    clock).Handle(
                        new PollKohChallenge(
                            fixture.CompetitionId,
                            fixture.CompetitionChallengeId,
                            1,
                            0,
                            fixture.DueAt,
                            fixture.DueAt.AddSeconds(20)),
                        cancellationToken);
                await Assert.That(staleEpoch).IsNull();
            }
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var dueAt = observedAt.AddTicks(
            -(observedAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        const string flag = "NOCTF{raw-koh-control}";
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.Shared,
            "registry.example/hill:v1",
            PortMappings: new Dictionary<int, int> { [8080] = 0 },
            Limits: new(268_435_456, 500_000_000, 128),
            Security: new(true, true, true, ["ALL"], []),
            UrlBindings:
            [
                new("http://{HOST}:{PORT}/play", RuntimeExposure.Participants, ContainerPort: 8080)
            ],
            ControlCheckUrlBinding: new(
                "http://{HOST}:{PORT}/control",
                RuntimeExposure.OwnerOnly,
                ContainerPort: 8080));
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "koh-owner",
            NormalizedUserName = "KOH-OWNER",
            Email = "koh-owner@example.test",
            NormalizedEmail = "KOH-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = dueAt,
            UpdatedAt = dueAt
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "KoH",
            OwnerId = ownerId,
            Mode = GameMode.Koh,
            Status = CompetitionStatus.Running,
            ConfigurationJson = JsonSerializer.Serialize(
                new KohConfiguration(1, 5, 10),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RunningSince = dueAt,
            StartAt = dueAt.AddHours(-1),
            EndAt = dueAt.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = dueAt,
            UpdatedAt = dueAt,
            ConfigurationUpdatedAt = dueAt
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "Hill",
            CreatedAt = dueAt,
            UpdatedAt = dueAt
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            ConfigurationJson = JsonSerializer.Serialize(
                new KohChallengeConfiguration(1, runtime),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = dueAt
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Blue",
            NormalizedName = "BLUE",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = "0123456789abcdef0123456789abcdef",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = dueAt
        });
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = Guid.CreateVersion7(),
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Flag = flag,
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            CreatedAt = dueAt
        });
        var runtimeId = Guid.CreateVersion7();
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = null,
            Purpose = RuntimePurpose.Player,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "default",
            RunnerId = "runner-a",
            State = RuntimeState.Queued,
            CreatedAt = dueAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(dueAt, competitionId, competitionChallengeId, teamId, runtimeId, flag);
    }

    private sealed record Fixture(
        DateTimeOffset DueAt,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        Guid RuntimeId,
        string Flag);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedKohControlClient(string flag) : IKohControlClient
    {
        public Task<KohControlResponse> ObserveAsync(
            Uri controlUrl,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(KohControlResponse.Success(Encoding.UTF8.GetBytes(flag)));
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<object> RunnerPoolMessages { get; } = [];
        public List<(object Message, DateTimeOffset At)> Scheduled { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Scheduled.Add((message!, scheduledAt));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage
        {
            RunnerPoolMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => throw new NotSupportedException();

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            throw new NotSupportedException();

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => throw new NotSupportedException();

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
