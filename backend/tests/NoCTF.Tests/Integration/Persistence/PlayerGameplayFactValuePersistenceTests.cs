using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.GameplayFacts.Management;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class PlayerGameplayFactValuePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Participant_reads_only_flag_values_submitted_by_their_team(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_player_fact_value")
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
            var store = new GameplayFactManagementStore(
                db,
                Substitute.For<ITransactionalMessageOutbox>());

            var ownFlag = await store.ReadPlayerValueAsync(
                fixture.CompetitionId,
                fixture.FlagFactId,
                fixture.TeamMemberId,
                cancellationToken);
            var ownBreak = await store.ReadPlayerValueAsync(
                fixture.CompetitionId,
                fixture.BreakFactId,
                fixture.TeamMemberId,
                cancellationToken);
            var otherTeamFlag = await store.ReadPlayerValueAsync(
                fixture.CompetitionId,
                fixture.FlagFactId,
                fixture.OtherTeamMemberId,
                cancellationToken);
            var nonFlag = await store.ReadPlayerValueAsync(
                fixture.CompetitionId,
                fixture.FixFactId,
                fixture.TeamMemberId,
                cancellationToken);
            var wrongCompetition = await store.ReadPlayerValueAsync(
                Guid.CreateVersion7(),
                fixture.FlagFactId,
                fixture.TeamMemberId,
                cancellationToken);

            await Assert.That(ownFlag?.Value).IsEqualTo(fixture.FlagValue);
            await Assert.That(ownFlag?.Kind).IsEqualTo(GameplayFactKind.FlagAttempt);
            await Assert.That(ownBreak?.Value).IsEqualTo(fixture.BreakValue);
            await Assert.That(ownBreak?.Kind).IsEqualTo(GameplayFactKind.BreakAttempt);
            await Assert.That(otherTeamFlag).IsNull();
            await Assert.That(nonFlag).IsNull();
            await Assert.That(wrongCompetition).IsNull();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var teamMemberId = Guid.CreateVersion7();
        var otherTeamMemberId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var otherTeamId = Guid.CreateVersion7();
        var flagFactId = Guid.CreateVersion7();
        var breakFactId = Guid.CreateVersion7();
        var fixFactId = Guid.CreateVersion7();
        const string flagValue = "flag{participant-owned-value}";
        const string breakValue = "flag{participant-owned-break}";

        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        db.Users.AddRange(
            User(ownerId, "value-owner", UserRole.Organizer, now),
            User(teamMemberId, "value-member", UserRole.User, now),
            User(otherTeamMemberId, "value-other", UserRole.User, now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = "Player gameplay fact value",
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":2,"defaultScoreCurve":{"initialPoints":500,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}""",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddMinutes(-5),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.AddRange(
            Team(teamId, competitionId, teamMemberId, "Player Team", '1', now),
            Team(otherTeamId, competitionId, otherTeamMemberId, "Other Team", '2', now));
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = "Value challenge",
            Direction = "Web",
            DefinitionJson = """{"schemaVersion":2}""",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = """{"schemaVersion":2}""",
            UpdatedAt = now
        });
        db.GameplayFacts.AddRange(
            Fact(flagFactId, competitionId, competitionChallengeId, teamId,
                teamMemberId, GameplayFactKind.FlagAttempt, flagValue, now),
            Fact(breakFactId, competitionId, competitionChallengeId, teamId,
                teamMemberId, GameplayFactKind.BreakAttempt, breakValue, now.AddSeconds(1)),
            Fact(fixFactId, competitionId, competitionChallengeId, teamId,
                teamMemberId, GameplayFactKind.FixAttempt, null, now.AddSeconds(2)));
        await db.SaveChangesAsync(cancellationToken);

        return new(
            competitionId,
            teamMemberId,
            otherTeamMemberId,
            flagFactId,
            breakFactId,
            fixFactId,
            flagValue,
            breakValue);
    }

    private static User User(Guid id, string userName, UserRole role, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Team Team(
        Guid id,
        Guid competitionId,
        Guid memberId,
        string name,
        char invitationCharacter,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = memberId,
            MemberIds = [memberId],
            InvitationToken = new string(invitationCharacter, 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        };

    private static GameplayFact Fact(
        Guid id,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        Guid actorUserId,
        GameplayFactKind kind,
        string? value,
        DateTimeOffset occurredAt) =>
        new()
        {
            Id = id,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = actorUserId,
            Kind = kind,
            Value = value,
            ValueSha256 = value is null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(value)),
            OccurredAt = occurredAt,
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct,
            UpdatedAt = occurredAt
        };

    private sealed record Fixture(
        Guid CompetitionId,
        Guid TeamMemberId,
        Guid OtherTeamMemberId,
        Guid FlagFactId,
        Guid BreakFactId,
        Guid FixFactId,
        string FlagValue,
        string BreakValue);
}
