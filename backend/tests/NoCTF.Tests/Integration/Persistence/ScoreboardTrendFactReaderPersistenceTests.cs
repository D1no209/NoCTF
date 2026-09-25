using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ScoreboardTrendFactReaderPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Reader_returns_only_applied_adjustments_for_requested_teams_before_cutoff(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_scoreboard_trends")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.Parse("2026-08-27T00:00:00Z");
            var ownerId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var requestedTeamId = Guid.CreateVersion7();
            var otherTeamId = Guid.CreateVersion7();
            var requestedCaptainId = Guid.CreateVersion7();
            var otherCaptainId = Guid.CreateVersion7();
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            db.Users.AddRange(
                User(ownerId, "trend-owner", now),
                User(requestedCaptainId, "requested-captain", now),
                User(otherCaptainId, "other-captain", now));
            db.Competitions.Add(new CtfCompetition
            {
                Id = competitionId,
                OwnerId = ownerId,
                Title = "Trend test",
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(-1),
                EndAt = now.AddHours(1),
                Status = CompetitionStatus.Running,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Challenges.Add(new CtfChallenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                Visibility = ChallengeVisibility.Private,
                Title = "Trend challenge",
                Direction = "PWN",
                Definition = TestConfigurations.Definition(GameMode.Ctf),
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CtfCompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                IsPublished = true,
                Rules = TestConfigurations.Rules(GameMode.Ctf),
                UpdatedAt = now
            });
            db.Teams.AddRange(
                Team(requestedTeamId, competitionId, requestedCaptainId, "Requested", now),
                Team(otherTeamId, competitionId, otherCaptainId, "Other", now));
            db.GameplayFacts.AddRange(
                Adjustment(competitionId, competitionChallengeId, requestedTeamId, ownerId, now, "25"),
                Adjustment(competitionId, competitionChallengeId, otherTeamId, ownerId, now, "40"),
                Adjustment(competitionId, competitionChallengeId, requestedTeamId, ownerId, now.AddHours(1), "50"),
                new ManualAdjustmentGameplayFact
                {
                    Id = Guid.CreateVersion7(),
                    CompetitionId = competitionId,
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = requestedTeamId,
                    ActorUserId = ownerId,
                    OccurredAt = now.AddMinutes(-1),
                    Value = "10",
                    State = GameplayFactState.Queued,
                    UpdatedAt = now.AddMinutes(-1)
                });
            await db.SaveChangesAsync(cancellationToken);

            var rows = await new ScoreboardTrendFactReader(db).ReadManualAdjustmentsAsync(
                competitionId,
                [requestedTeamId],
                now.AddMinutes(1),
                cancellationToken);

            await Assert.That(rows).Count().IsEqualTo(1);
            await Assert.That(rows[0].TeamId).IsEqualTo(requestedTeamId);
            await Assert.That(rows[0].Delta).IsEqualTo(25);
        });
    }

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        Kind = UserKind.Human,
        Role = UserRole.Organizer,
        EmailVerifiedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Team Team(
        Guid id,
        Guid competitionId,
        Guid captainId,
        string name,
        DateTimeOffset now) => new()
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = captainId,
            MemberIds = [captainId],
            InvitationToken = id.ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        };

    private static GameplayFact Adjustment(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        Guid actorId,
        DateTimeOffset occurredAt,
        string value) => new ManualAdjustmentGameplayFact
        {
            Id = Guid.CreateVersion7(),
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = actorId,
            OccurredAt = occurredAt,
            Value = value,
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Applied,
            UpdatedAt = occurredAt
        };
}
