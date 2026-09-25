using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Scoring;
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("AwdpRoundScoring")]
public sealed class AwdpParticipantStatePersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(300_000)]
    public async Task Participant_state_restores_runtime_activations_and_fix_stage(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var now = DateTimeOffset.UtcNow;
            var fixture = await SeedParticipantStateAsync(options, now, cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var state = await new AwdpParticipantStateReader(db).FindAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserId,
                now,
                cancellationToken);

            await Assert.That(state).IsNotNull();
            await Assert.That(state!.CurrentRound).IsEqualTo(3);
            await Assert.That(state.AttackRuntime?.Id).IsEqualTo(fixture.AttackRuntimeId);
            await Assert.That(state.AttackRuntime?.Id).IsEqualTo(fixture.AttackRuntimeId);
            await Assert.That(state.BreakActivation?.GameplayFactId).IsEqualTo(fixture.BreakFactId);
            await Assert.That(state.BreakActivation?.EffectiveRound).IsEqualTo(1);
            await Assert.That(state.LatestBreakAttempt?.Result).IsEqualTo(GameplayFactResult.Wrong);
            await Assert.That(state.Defense.GameplayFactId).IsEqualTo(fixture.FixFactId);
            await Assert.That(state.Defense.RuntimeState).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(state.Defense.Result).IsEqualTo(GameplayFactResult.Correct);
            await Assert.That(state.FixActivation?.EffectiveRound).IsEqualTo(2);
            await Assert.That(state.MaximumFixAttempts).IsEqualTo(10);
            await Assert.That(state.AcceptedFixAttempts).IsEqualTo(1);
            await Assert.That(state.RemainingFixAttempts).IsEqualTo(9);
            var attempts = await new FlagAttemptStateReader(db).ReadAsync(
                fixture.CompetitionId, fixture.CompetitionChallengeId, fixture.TeamId,
                GameMode.Awdp, CompetitionStatus.Running, cancellationToken);
            await Assert.That(attempts?.Maximum).IsEqualTo(10);
            await Assert.That(attempts?.Accepted).IsEqualTo(2);
            await Assert.That(attempts?.Remaining).IsEqualTo(8);
            await Assert.That(attempts?.Solved).IsFalse();

            var outsider = await new AwdpParticipantStateReader(db).FindAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                Guid.NewGuid(),
                now,
                cancellationToken);
            await Assert.That(outsider).IsNull();
            var competition = await db.Competitions.SingleAsync(x => x.Id == fixture.CompetitionId, cancellationToken);
            competition.Status = CompetitionStatus.Finished;
            AddLifecycle(db, fixture.CompetitionId, CompetitionStatus.Running, CompetitionStatus.Finished, now.AddSeconds(20));
            await db.SaveChangesAsync(cancellationToken);
            var finished = await new AwdpParticipantStateReader(db).FindAsync(fixture.CompetitionId,
                fixture.CompetitionChallengeId, fixture.UserId, now.AddDays(1), cancellationToken);
            await Assert.That(finished!.CurrentRound).IsEqualTo(3);
        });
    }

    private static async Task<ParticipantFixture> SeedParticipantStateAsync(
        DbContextOptions<NoCtfDbContext> options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var userId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now.AddMilliseconds(1));
        var challengeId = Guid.CreateVersion7(now.AddMilliseconds(2));
        var competitionChallengeId = Guid.CreateVersion7(now.AddMilliseconds(3));
        var teamId = Guid.CreateVersion7(now.AddMilliseconds(4));
        var attackRuntimeId = Guid.CreateVersion7(now.AddMilliseconds(5));
        var breakFactId = Guid.CreateVersion7(now.AddMilliseconds(6));
        var latestBreakFactId = Guid.CreateVersion7(now.AddMilliseconds(7));
        var fixFactId = Guid.CreateVersion7(now.AddMilliseconds(8));
        var fixRuntimeId = Guid.CreateVersion7(now.AddMilliseconds(9));
        var platformFailedFixFactId = Guid.CreateVersion7(now.AddMilliseconds(10));
        db.Users.Add(User(userId, "participant", now));
        db.Competitions.Add(new AwdpCompetition
        {
            Id = competitionId,
            Title = "AWDP participant state",
            OwnerId = userId,
            Status = CompetitionStatus.Running,
            ModeConfiguration = TestConfigurations.Competition(
                GameMode.Awdp,
                RoundConfiguration(roundDurationSeconds: 60)),
            StartAt = now.AddMinutes(-10),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
            MaxConcurrentRuntimeInstancesPerTeam = 2,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new AwdpChallenge
        {
            Id = challengeId,
            OwnerId = userId,
            Title = "AWDP state",
            Definition = TestConfigurations.Definition(GameMode.Awdp),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new AwdpCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            Rules = TestConfigurations.Rules(GameMode.Awdp),
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "State Team",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('s', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        AddLifecycle(db, competitionId, CompetitionStatus.Visible, CompetitionStatus.Running, now.AddSeconds(-190));
        AddLifecycle(db, competitionId, CompetitionStatus.Running, CompetitionStatus.Paused, now.AddSeconds(-130));
        AddLifecycle(db, competitionId, CompetitionStatus.Paused, CompetitionStatus.Running, now.AddSeconds(-100));
        db.GameplayFacts.AddRange(
            Fact(breakFactId, competitionId, competitionChallengeId, teamId, userId,
                GameplayFactKind.BreakAttempt, GameplayFactResult.Correct, now.AddSeconds(-150)),
            Fact(latestBreakFactId, competitionId, competitionChallengeId, teamId, userId,
                GameplayFactKind.BreakAttempt, GameplayFactResult.Wrong, now.AddSeconds(-10)),
            Fact(fixFactId, competitionId, competitionChallengeId, teamId, userId,
                GameplayFactKind.FixAttempt, GameplayFactResult.Correct, now.AddSeconds(-70),
                GameplayFactReferenceKind.PatchUpload, Guid.CreateVersion7(now.AddMilliseconds(11))),
            new FixAttemptGameplayFact
            {
                Id = platformFailedFixFactId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                ActorUserId = userId,
                ReferenceKind = GameplayFactReferenceKind.PatchUpload,
                ReferenceId = Guid.CreateVersion7(now.AddMilliseconds(12)),
                State = GameplayFactState.PlatformFailed,
                FailureCode = GameplayFactFailureCode.CheckerPlatformError,
                OccurredAt = now.AddSeconds(-20),
                UpdatedAt = now.AddSeconds(-20)
            });
        db.RuntimeInstances.AddRange(
            new AwdpAttackRuntimeInstance
            {
                Id = attackRuntimeId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerId = "runner-state",
                State = RuntimeState.Running,
                ProviderReceipt = RuntimeReceiptTestData.ContainerEntity(),
                AccessEndpoints = [new RuntimeAccessEndpoint
                {
                    BindingIndex = 0,
                    DirectAddress = "tcp://127.0.0.1:31000"
                }],
                CreatedAt = now.AddMinutes(-2),
                RunningAt = now.AddMinutes(-2),
                ExpiresAt = now.AddMinutes(10)
            },
            new AwdpTargetRuntimeInstance
            {
                Id = fixRuntimeId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                GameplayFactId = fixFactId,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Stopped,
                CreatedAt = now.AddSeconds(-75),
                RunningAt = now.AddSeconds(-74),
                StoppedAt = now.AddSeconds(-65)
            });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            userId,
            competitionId,
            competitionChallengeId,
            teamId,
            attackRuntimeId,
            breakFactId,
            fixFactId);
    }

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private static GameplayFact Fact(
        Guid id,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        Guid actorUserId,
        GameplayFactKind kind,
        GameplayFactResult result,
        DateTimeOffset occurredAt,
        GameplayFactReferenceKind? referenceKind = null,
        Guid? referenceId = null)
    {
        var fact = GameplayFactGeneratedCatalog.Create(kind);
        fact.Id = id;
        fact.CompetitionId = competitionId;
        fact.CompetitionChallengeId = competitionChallengeId;
        fact.TeamId = teamId;
        fact.ActorUserId = actorUserId;
        fact.ReferenceKind = referenceKind;
        fact.ReferenceId = referenceId;
        fact.State = GameplayFactState.Completed;
        fact.Result = result;
        fact.Value = kind == GameplayFactKind.BreakAttempt ? $"flag{{{id:N}}}" : null;
        fact.ValueSha256 = kind == GameplayFactKind.BreakAttempt
            ? SHA256.HashData(Encoding.UTF8.GetBytes($"flag{{{id:N}}}"))
            : null;
        fact.OccurredAt = occurredAt;
        fact.UpdatedAt = occurredAt;
        return fact;
    }

    private static void AddLifecycle(
        NoCtfDbContext db,
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        DateTimeOffset occurredAt) =>
        db.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent
        {
            Id = Guid.CreateVersion7(occurredAt),
            CompetitionId = competitionId,
            Level = CompetitionEventLevel.Information,
            Visibility = CompetitionEventVisibility.Public,
            SubjectType = EntityReferenceKind.Competition,
            SubjectId = competitionId,
            PreviousCompetitionStatus = from,
            CompetitionStatus = to,
            Automatic = false,
            OccurredAt = occurredAt
        });

    private static string RoundConfiguration(int roundDurationSeconds) => JsonSerializer.Serialize(
        new AwdpConfiguration(
            roundDurationSeconds,
            new(100, 100, 2, ScoreDecayMode.Fixed),
            new(50, 50, 2, ScoreDecayMode.Fixed),
            RequireBreakBeforeFix: false),
        JsonOptions);

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_awdp_state_{Guid.NewGuid():N}")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private sealed record ParticipantFixture(
        Guid UserId,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        Guid AttackRuntimeId,
        Guid BreakFactId,
        Guid FixFactId);

}
