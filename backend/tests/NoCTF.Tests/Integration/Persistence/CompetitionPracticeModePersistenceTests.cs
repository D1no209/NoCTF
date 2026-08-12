using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.GameplayFacts.Practice;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.GameplayFacts.Practice;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[NotInParallel]
public sealed class CompetitionPracticeModePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Finished_ctf_practice_runtime_can_judge_flags_without_scoring(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_practice")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, ct);

            await using (var history = new NoCtfDbContext(options))
            {
                history.RuntimeInstances.Add(Runtime(
                    fixture,
                    RuntimeState.Stopped,
                    RuntimePurpose.Player));
                await history.SaveChangesAsync(ct);
            }

            var outbox = new RecordingOutbox();
            await using (var db = new NoCtfDbContext(options))
            {
                var store = new RuntimeInstanceStore(
                    db,
                    new ChallengeRuntimeTemplateCatalog(),
                    new FixedRuntimePlacementPolicy(runnerPool: "practice-tests"),
                    new PostgresPerTeamRuntimeFlagStore(db),
                    outbox);
                var started = await store.MutatePlayerRuntimeAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.UserId,
                    RuntimeAction.Start,
                    null,
                    fixture.Now), ct);

                await Assert.That(started.Failure).IsNull();
                await Assert.That(started.Runtime).IsNotNull();
                await Assert.That(outbox.Published.OfType<DispatchRuntime>()).HasSingleItem();
                var runtime = await db.RuntimeInstances.SingleAsync(
                    item => item.Purpose == RuntimePurpose.Practice,
                    ct);
                await Assert.That(runtime.Generation).IsEqualTo(2);
                await Assert.That(runtime.Purpose).IsEqualTo(RuntimePurpose.Practice);
                runtime.State = RuntimeState.Running;
                runtime.RunningAt = fixture.Now;
                runtime.ExpiresAt = fixture.Now.AddMinutes(30);
                await db.SaveChangesAsync(ct);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var judge = new JudgePracticeFlag(new PracticeFlagJudge(db));
                var correct = await judge.ExecuteAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.UserId,
                    fixture.Flag,
                    fixture.Now.AddMinutes(1)), ct);
                var wrong = await judge.ExecuteAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.UserId,
                    "flag{wrong}",
                    fixture.Now.AddMinutes(1)), ct);

                await Assert.That(correct.Judgement)
                    .IsEqualTo(PracticeFlagJudgement.Correct);
                await Assert.That(wrong.Judgement)
                    .IsEqualTo(PracticeFlagJudgement.Wrong);
                await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
                await Assert.That(await db.CompetitionEvents.CountAsync(ct)).IsEqualTo(0);
                await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(0);
                await Assert.That(await db.Competitions
                    .Select(item => item.LeaderboardDirty)
                    .SingleAsync(ct)).IsFalse();
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var team = await db.Teams.SingleAsync(ct);
                team.IsBanned = true;
                await db.SaveChangesAsync(ct);
                var rejected = await new JudgePracticeFlag(new PracticeFlagJudge(db))
                    .ExecuteAsync(new(
                        fixture.CompetitionId,
                        fixture.CompetitionChallengeId,
                        fixture.UserId,
                        fixture.Flag,
                        fixture.Now.AddMinutes(2)), ct);
                await Assert.That(rejected.FailureCode)
                    .IsEqualTo(PracticeFlagFailureCode.TeamNotEligible);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Practice_mode_cannot_be_disabled_while_a_practice_runtime_is_active(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_practice_disable")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            db.RuntimeInstances.Add(Runtime(fixture, RuntimeState.Running));
            await db.SaveChangesAsync(ct);
            var store = new CompetitionManagementStore(db);
            var current = await store.FindAsync(fixture.CompetitionId, true, ct);
            var updated = await store.UpdateAsync(new(
                fixture.CompetitionId,
                current!.Title,
                current.Description,
                current.StartTime,
                current.EndTime,
                current.TeamRegistrationAutoApprove,
                current.MaxTeamMembers,
                current.MaxConcurrentRuntimeInstancesPerTeam,
                fixture.OwnerId,
                fixture.Now.AddMinutes(1),
                current.AllowTeamRegistrationWhileRunning,
                current.MaxActiveQuestionsPerTeam,
                current.MaxParticipantMessagesBeforeHandlerReply,
                current.AllowChallengeOwnersToHandleQuestions,
                PracticeModeEnabled: false), current.Status, ct);

            await Assert.That(updated).IsNull();
            await Assert.That(await db.Competitions
                .Select(item => item.PracticeModeEnabled)
                .SingleAsync(ct)).IsTrue();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7(now);
        var userId = Guid.CreateVersion7(now.AddTicks(1));
        var competitionId = Guid.CreateVersion7(now.AddTicks(2));
        var challengeId = Guid.CreateVersion7(now.AddTicks(3));
        var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(4));
        var teamId = Guid.CreateVersion7(now.AddTicks(5));
        const string flag = "flag{practice-is-unscored}";
        db.Users.AddRange(User(ownerId, "practice-owner", now), User(userId, "practice-player", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = "Finished practice competition",
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Finished,
            PracticeModeEnabled = true,
            LeaderboardDirty = false,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            ConfigurationJson = """{"schemaVersion":1}""",
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-2),
            EndAt = now.AddHours(-1),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = "Practice container",
            Direction = "Web",
            DefinitionJson = JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    null,
                    Runtime: new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition("registry.example/practice:v1"),
                        new RuntimeResourceLimits(67_108_864, 100_000_000, 64))),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 500,
            IsPublished = true,
            RulesJson = """{"schemaVersion":1}""",
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Practice Team",
            NormalizedName = "PRACTICE TEAM",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = "0123456789abcdefghijklmnopqrstuv",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = Guid.CreateVersion7(now.AddTicks(6)),
            CompetitionChallengeId = competitionChallengeId,
            Flag = flag,
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        return new(now, ownerId, userId, competitionId, challengeId, competitionChallengeId, teamId, flag);
    }

    private static RuntimeInstance Runtime(
        Fixture fixture,
        RuntimeState state,
        RuntimePurpose purpose = RuntimePurpose.Practice) => new()
    {
        Id = Guid.CreateVersion7(fixture.Now.AddTicks(10)),
        CompetitionId = fixture.CompetitionId,
        CompetitionChallengeId = fixture.CompetitionChallengeId,
        TeamId = fixture.TeamId,
        Purpose = purpose,
        Generation = 1,
        RuntimeKind = RuntimeKind.Container,
        RuntimeProvider = RuntimeProvider.Docker,
        RunnerPool = "practice-tests",
        State = state,
        CreatedAt = fixture.Now,
        RunningAt = state == RuntimeState.Running ? fixture.Now : null,
        ExpiresAt = fixture.Now.AddMinutes(30)
    };

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

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid OwnerId,
        Guid UserId,
        Guid CompetitionId,
        Guid ChallengeId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        string Flag);

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt) where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
