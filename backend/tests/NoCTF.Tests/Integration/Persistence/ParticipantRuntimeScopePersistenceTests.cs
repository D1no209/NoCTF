using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
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
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Runtime.Targets;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ParticipantRuntimeScopePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Participant_runtime_queries_exclude_unpublished_and_soft_deleted_resources(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_participant_runtime_scope")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var instances = new RuntimeInstanceStore(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(),
                new UnexpectedRuntimeFlagStore(),
                new NoopOutbox());
            var targets = new RuntimeTargetReader(db);
            var observations = new List<InvalidScopeObservation>();
            foreach (var scope in fixture.InvalidScopes)
            {
                var current = await instances.FindPlayerRuntimeAsync(
                    scope.CompetitionId,
                    scope.CompetitionChallengeId,
                    scope.UserId,
                    cancellationToken);
                var mutation = await instances.MutatePlayerRuntimeAsync(
                    new RuntimeMutationCommand(
                        scope.CompetitionId,
                        scope.CompetitionChallengeId,
                        scope.UserId,
                        RuntimeAction.Reset,
                        null,
                        fixture.Now),
                    cancellationToken);
                var listedTargets = await targets.ListAsync(
                    scope.CompetitionId,
                    scope.CompetitionChallengeId,
                    scope.UserId,
                    fixture.Now,
                    cancellationToken);
                observations.Add(new(
                    scope.Name,
                    current is not null,
                    mutation.Runtime is not null || mutation.Failure != RuntimeMutationFailure.NotFound,
                    listedTargets is not null));
            }

            await Assert.That(observations
                    .Where(observation => observation.RuntimeWasVisible)
                    .Select(observation => observation.Name))
                .IsEmpty();
            await Assert.That(observations
                    .Where(observation => observation.MutationWasAccepted)
                    .Select(observation => observation.Name))
                .IsEmpty();
            await Assert.That(observations
                    .Where(observation => observation.TargetsWereVisible)
                    .Select(observation => observation.Name))
                .IsEmpty();

            var validRuntime = await instances.FindPlayerRuntimeAsync(
                fixture.ValidScope.CompetitionId,
                fixture.ValidScope.CompetitionChallengeId,
                fixture.ValidScope.UserId,
                cancellationToken);
            var validTargets = await targets.ListAsync(
                fixture.ValidScope.CompetitionId,
                fixture.ValidScope.CompetitionChallengeId,
                fixture.ValidScope.UserId,
                fixture.Now,
                cancellationToken);
            var visibleTargets = validTargets
                ?? throw new InvalidOperationException("The valid AWD target scope was rejected.");

            await Assert.That(validRuntime).IsNotNull();
            await Assert.That(visibleTargets).HasSingleItem();
            await Assert.That(visibleTargets[0].TeamId).IsEqualTo(fixture.ValidScope.TeamId);
            await Assert.That(visibleTargets[0].Urls)
                .IsEquivalentTo(["https://active-team.example.test"]);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        var challengeDefinition = JsonSerializer.Serialize(
            new AwdChallengeConfiguration(
                AwdChallengeConfiguration.CurrentSchemaVersion,
                Runtime: new ChallengeRuntimeTemplate(
                    RuntimeAllocation.PerTeam,
                    new ContainerRuntimeDefinition("scope-test:latest"),
                    new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                    FlagSource: RuntimeFlagSource.AwdRotation),
                FlagInjection: new AwdFlagInjectionConfiguration(
                    "printf '%s' '${FLAG}' > /dev/shm/flag")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var challengeRules = JsonSerializer.Serialize(
            new AwdChallengeConfiguration(AwdChallengeConfiguration.CurrentSchemaVersion),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var competitionConfiguration = JsonSerializer.Serialize(
            AwdConfiguration.Default,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var invalidScopes = new[]
        {
            AddScope(
                db,
                "unpublished-challenge",
                challengeDefinition,
                challengeRules,
                competitionConfiguration,
                now,
                published: false),
            AddScope(
                db,
                "deleted-competition-challenge",
                challengeDefinition,
                challengeRules,
                competitionConfiguration,
                now,
                competitionChallengeDeletedAt: now),
            AddScope(
                db,
                "deleted-challenge-template",
                challengeDefinition,
                challengeRules,
                competitionConfiguration,
                now,
                challengeDeletedAt: now),
            AddScope(
                db,
                "deleted-team",
                challengeDefinition,
                challengeRules,
                competitionConfiguration,
                now,
                teamDeletedAt: now),
            AddScope(
                db,
                "deleted-competition",
                challengeDefinition,
                challengeRules,
                competitionConfiguration,
                now,
                competitionDeletedAt: now)
        };
        var validScope = AddScope(
            db,
            "valid-scope",
            challengeDefinition,
            challengeRules,
            competitionConfiguration,
            now,
            runtimeUrl: "https://active-team.example.test");
        _ = AddTeam(
            db,
            validScope.CompetitionId,
            "deleted-target",
            now,
            deletedAt: now,
            runtime: new(
                validScope.CompetitionId,
                validScope.CompetitionChallengeId,
                "https://deleted-team.example.test"));

        await db.SaveChangesAsync(cancellationToken);
        return new(now, invalidScopes, validScope);
    }

    private static RuntimeScopeFixture AddScope(
        NoCtfDbContext db,
        string name,
        string challengeDefinition,
        string challengeRules,
        string competitionConfiguration,
        DateTimeOffset now,
        bool published = true,
        DateTimeOffset? competitionDeletedAt = null,
        DateTimeOffset? competitionChallengeDeletedAt = null,
        DateTimeOffset? challengeDeletedAt = null,
        DateTimeOffset? teamDeletedAt = null,
        string? runtimeUrl = null)
    {
        var userId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now);
        var challengeId = Guid.CreateVersion7(now);
        var competitionChallengeId = Guid.CreateVersion7(now);
        db.Users.Add(NewUser(userId, name, now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = name,
            OwnerId = userId,
            Mode = GameMode.Awd,
            Status = CompetitionStatus.Running,
            ConfigurationJson = competitionConfiguration,
            RunningSince = now.AddMinutes(-1),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now,
            DeletedAt = competitionDeletedAt
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = userId,
            Mode = GameMode.Awd,
            Title = name,
            DefinitionJson = challengeDefinition,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = challengeDeletedAt
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = published,
            RulesJson = challengeRules,
            UpdatedAt = now,
            DeletedAt = competitionChallengeDeletedAt
        });
        var team = AddTeam(
            db,
            competitionId,
            name,
            now,
            deletedAt: teamDeletedAt,
            runtime: new(
                competitionId,
                competitionChallengeId,
                runtimeUrl ?? $"https://{name}.example.test"));
        return new(name, competitionId, competitionChallengeId, team.UserId, team.TeamId);
    }

    private static TeamFixture AddTeam(
        NoCtfDbContext db,
        Guid competitionId,
        string name,
        DateTimeOffset now,
        DateTimeOffset? deletedAt,
        RuntimeFixture runtime)
    {
        var userId = Guid.CreateVersion7(now);
        var teamId = Guid.CreateVersion7(now);
        db.Users.Add(NewUser(userId, $"{name}-member", now));
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now,
            DeletedAt = deletedAt
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = Guid.CreateVersion7(now),
            CompetitionId = runtime.CompetitionId,
            CompetitionChallengeId = runtime.CompetitionChallengeId,
            TeamId = teamId,
            Purpose = RuntimePurpose.Player,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "scope-tests",
            State = RuntimeState.Running,
            ProcessingVersion = 1,
            Urls = [runtime.Url],
            ParticipantUrlIndexes = [0],
            CreatedAt = now.AddMinutes(-1),
            RunningAt = now.AddSeconds(-30)
        });
        return new(userId, teamId);
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

    private sealed record Fixture(
        DateTimeOffset Now,
        IReadOnlyList<RuntimeScopeFixture> InvalidScopes,
        RuntimeScopeFixture ValidScope);

    private sealed record RuntimeScopeFixture(
        string Name,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid UserId,
        Guid TeamId);

    private sealed record TeamFixture(Guid UserId, Guid TeamId);
    private sealed record RuntimeFixture(
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        string Url);

    private sealed record InvalidScopeObservation(
        string Name,
        bool RuntimeWasVisible,
        bool MutationWasAccepted,
        bool TargetsWereVisible);

    private sealed class UnexpectedRuntimeFlagStore : IPerTeamRuntimeFlagStore
    {
        public Task<string> EnsureAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid teamId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AWD runtime reset must not request a CTF flag.");
    }

    private sealed class NoopOutbox : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
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
