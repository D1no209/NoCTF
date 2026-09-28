using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
            var pendingScope = fixture.InvalidScopes.Single(scope => scope.Name == "pending-team");
            var retainedPendingRuntime = await db.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.TeamId == pendingScope.TeamId,
                cancellationToken);
            await Assert.That(retainedPendingRuntime.State).IsEqualTo(RuntimeState.Running);

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
            await Assert.That(visibleTargets[0].AccessEndpoints?.Select(endpoint => endpoint.DirectAddress).OfType<string>())
                .IsEquivalentTo(["https://active-team.example.test"]);

            var existingTemplateId = await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challenge.Id == fixture.ValidScope.CompetitionChallengeId)
                .Select(challenge => challenge.ChallengeId)
                .SingleAsync(cancellationToken);
            var ownerId = await db.Challenges.AsNoTracking()
                .Where(template => template.Id == existingTemplateId)
                .Select(template => template.OwnerId)
                .SingleAsync(cancellationToken);
            var templateId = Guid.CreateVersion7();
            var unstartedChallengeId = Guid.CreateVersion7();
            var nextOrder = await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challenge.CompetitionId == fixture.ValidScope.CompetitionId)
                .MaxAsync(challenge => challenge.Order, cancellationToken) + 1;
            db.Challenges.Add(new AwdChallenge
            {
                Id = templateId,
                OwnerId = ownerId,
                Title = "Unstarted Runtime template",
                Direction = "Web",
                Definition = TestConfigurations.Definition(GameMode.Awd),
                CreatedAt = fixture.Now,
                UpdatedAt = fixture.Now
            });
            db.CompetitionChallenges.Add(new AwdCompetitionChallenge
            {
                Id = unstartedChallengeId,
                CompetitionId = fixture.ValidScope.CompetitionId,
                ChallengeId = templateId,
                Order = nextOrder,
                IsPublished = true,
                Rules = TestConfigurations.Rules(GameMode.Awd),
                UpdatedAt = fixture.Now
            });
            await db.SaveChangesAsync(cancellationToken);
            var counter = new QueryCounter();
            var measuredOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(counter)
                .Options;
            await using var measuredDb = new NoCtfDbContext(measuredOptions);
            var measuredInstances = new RuntimeInstanceStore(
                measuredDb,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(),
                new UnexpectedRuntimeFlagStore(),
                new NoopOutbox());
            await Assert.That(await measuredInstances.FindPlayerRuntimeAsync(
                fixture.ValidScope.CompetitionId,
                unstartedChallengeId,
                fixture.ValidScope.UserId,
                cancellationToken)).IsNull();
            await Assert.That(counter.ReaderCount).IsLessThanOrEqualTo(2);
        });
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            ReaderCount++;
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        var challengeDefinition = JsonSerializer.Serialize(
            new AwdChallengeConfiguration(
                Runtime: new ChallengeRuntimeTemplate(
                    RuntimeAllocation.PerTeam,
                    new ContainerRuntimeDefinition(
                        "scope-test:latest",
                        PortMappings: new Dictionary<int, int> { [31337] = 0 },
                        Security: new(false, false, false, ["ALL"], [])),
                    new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                    UrlBindings:
                    [
                        new RuntimeUrlBinding(
                            "tcp://{HOST}:{PORT}",
                            RuntimeExposure.Participants,
                            ContainerPort: 31337)
                    ],
                    FlagSource: RuntimeFlagSource.AwdRotation),
                FlagInjection: new AwdFlagInjectionConfiguration(
                    "printf '%s' '${FLAG}' > /dev/shm/flag")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var challengeRules = JsonSerializer.Serialize(
            new AwdChallengeConfiguration(),
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
                "pending-team",
                challengeDefinition,
                challengeRules,
                competitionConfiguration,
                now,
                registrationStatus: TeamRegistrationStatus.Pending),
            AddScope(
                db,
                "unregistered-team",
                challengeDefinition,
                challengeRules,
                competitionConfiguration,
                now,
                registrationStatus: TeamRegistrationStatus.Unregistered),
            AddScope(
                db,
                "banned-team",
                challengeDefinition,
                challengeRules,
                competitionConfiguration,
                now,
                teamBanned: true),
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
        TeamRegistrationStatus registrationStatus = TeamRegistrationStatus.Approved,
        bool teamBanned = false,
        string? runtimeUrl = null)
    {
        var userId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now);
        var challengeId = Guid.CreateVersion7(now);
        var competitionChallengeId = Guid.CreateVersion7(now);
        db.Users.Add(NewUser(userId, name, now));
        db.Competitions.Add(new AwdCompetition
        {
            Id = competitionId,
            Title = name,
            OwnerId = userId,
            Status = CompetitionStatus.Running,
            ModeConfiguration = TestConfigurations.Competition(
                GameMode.Awd,
                competitionConfiguration),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = competitionDeletedAt
        });
        db.Challenges.Add(new AwdChallenge
        {
            Id = challengeId,
            OwnerId = userId,
            Title = name,
            Definition = TestConfigurations.Definition(GameMode.Awd, challengeDefinition),
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = challengeDeletedAt
        });
        db.CompetitionChallenges.Add(new AwdCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = published,
            Rules = TestConfigurations.Rules(GameMode.Awd, challengeRules),
            UpdatedAt = now,
            DeletedAt = competitionChallengeDeletedAt
        });
        var team = AddTeam(
            db,
            competitionId,
            name,
            now,
            runtime: new(
                competitionId,
                competitionChallengeId,
                runtimeUrl ?? $"https://{name}.example.test"),
            deletedAt: teamDeletedAt,
            registrationStatus: registrationStatus,
            banned: teamBanned);
        return new(name, competitionId, competitionChallengeId, team.UserId, team.TeamId);
    }

    private static TeamFixture AddTeam(
        NoCtfDbContext db,
        Guid competitionId,
        string name,
        DateTimeOffset now,
        RuntimeFixture runtime,
        DateTimeOffset? deletedAt = null,
        TeamRegistrationStatus registrationStatus = TeamRegistrationStatus.Approved,
        bool banned = false)
    {
        var userId = Guid.CreateVersion7(now);
        var teamId = Guid.CreateVersion7(now);
        db.Users.Add(NewUser(userId, $"{name}-member", now));
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = registrationStatus,
            RegisteredAt = now,
            IsBanned = banned,
            DeletedAt = deletedAt
        });
        db.RuntimeInstances.Add(new PlayerRuntimeInstance
        {
            Id = Guid.CreateVersion7(now),
            CompetitionId = runtime.CompetitionId,
            CompetitionChallengeId = runtime.CompetitionChallengeId,
            TeamId = teamId,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            State = RuntimeState.Running,
            AccessEndpoints = [new RuntimeAccessEndpoint
            {
                BindingIndex = 0,
                DirectAddress = runtime.Url
            }],
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

        public Task<string> EnsureRuntimeInstanceAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid teamId,
            Guid runtimeInstanceId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AWD runtime reset must not request an AWDP flag.");

        public Task InvalidateRuntimeInstanceAsync(
            Guid runtimeInstanceId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AWD runtime reset must not invalidate an AWDP flag.");
    }

    private sealed class NoopOutbox : IPostCommitMessagePublisher
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
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
