using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Hints;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ParticipantChallengeHintPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Published_hint_content_requires_a_completed_unlock_by_the_current_eligible_team(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_participant_hints").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var userA = User("hint-a", now);
            var userB = User("hint-b", now);
            var competition = new Competition
            {
                Id = Guid.NewGuid(), OwnerId = userA.Id, Title = "Hints", Mode = GameMode.Ctf,
                ConfigurationJson = """{"schemaVersion":2}""", FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(-1), EndAt = now.AddHours(1), Status = CompetitionStatus.Running,
                CreatedAt = now, UpdatedAt = now
            };
            var template = new Challenge
            {
                Id = Guid.NewGuid(), OwnerId = userA.Id, Mode = GameMode.Ctf, Title = "Hint source", Direction = "Web",
                DefinitionJson = """{"schemaVersion":2}""", CreatedAt = now, UpdatedAt = now
            };
            var free = Hint(0, now);
            var paid = Hint(20, now);
            var hidden = Hint(0, now);
            hidden.HiddenAt = now;
            var challenge = new CompetitionChallenge
            {
                Id = Guid.NewGuid(), CompetitionId = competition.Id, ChallengeId = template.Id,
                IsPublished = true, RulesJson = """{"schemaVersion":2}""", UpdatedAt = now,
                Hints = [free, paid, Hint(0, null), Hint(0, now.AddHours(1)), hidden]
            };
            var teamA = Team(userA.Id, competition.Id, 'a', now);
            var teamB = Team(userB.Id, competition.Id, 'b', now);
            var unlockB = Unlock(competition.Id, challenge.Id, teamB.Id, userB.Id, paid.Id, now);
            var pendingA = Unlock(competition.Id, challenge.Id, teamA.Id, userA.Id, paid.Id, now);
            pendingA.State = GameplayFactState.Queued;
            db.Users.AddRange(userA, userB);
            db.Competitions.Add(competition);
            db.Challenges.Add(template);
            db.CompetitionChallenges.Add(challenge);
            db.Teams.AddRange(teamA, teamB);
            db.GameplayFacts.AddRange(unlockB, pendingA);
            await db.SaveChangesAsync(ct);
            var store = new ParticipantChallengeHintStore(db);
            var reader = new ReadParticipantChallengeHints(store);

            var first = await reader.ExecuteAsync(competition.Id, challenge.Id, userA.Id, now, ct);
            await Assert.That(first.Count).IsEqualTo(2);
            await Assert.That(first.Single(item => item.Id == paid.Id).Content).IsNull();
            await Assert.That(first.Single(item => item.Id == paid.Id).CanUnlock).IsTrue();
            var other = await reader.ExecuteAsync(competition.Id, challenge.Id, userB.Id, now, ct);
            await Assert.That(other.Single(item => item.Id == paid.Id).Content).IsEqualTo(paid.Content);
            var anonymous = await reader.ExecuteAsync(competition.Id, challenge.Id, Guid.Empty, now, ct);
            await Assert.That(anonymous.Single(item => item.Id == paid.Id).Content).IsNull();
            await Assert.That(anonymous.Single(item => item.Id == paid.Id).CanUnlock).IsFalse();

            pendingA.State = GameplayFactState.Completed;
            await db.SaveChangesAsync(ct);
            var unlocked = await reader.ExecuteAsync(competition.Id, challenge.Id, userA.Id, now, ct);
            await Assert.That(unlocked.Single(item => item.Id == paid.Id).Content).IsEqualTo(paid.Content);
            pendingA.Result = GameplayFactResult.Rejected;
            pendingA.FailureCode = GameplayFactFailureCode.InsufficientScore;
            await db.SaveChangesAsync(ct);
            var unlockStore = new ChallengeHintStore(db, Substitute.For<ITransactionalMessageOutbox>());
            var retry = await unlockStore.UnlockAsync(competition.Id, challenge.Id, paid.Id, userA.Id, now, ct);
            await Assert.That(retry.Result!.Created).IsTrue();
            await Assert.That(retry.Result.GameplayFactId).IsNotEqualTo(pendingA.Id);
            var duplicate = await unlockStore.UnlockAsync(competition.Id, challenge.Id, paid.Id, userA.Id, now, ct);
            await Assert.That(duplicate.Result!.Created).IsFalse();
            await Assert.That(duplicate.Result.GameplayFactId).IsEqualTo(retry.Result.GameplayFactId);
            competition.Status = CompetitionStatus.Finished;
            await db.SaveChangesAsync(ct);
            await Assert.That((await store.ReadAsync(competition.Id, challenge.Id, userA.Id, ct))!.CanUnlock).IsFalse();
            teamA.IsBanned = true;
            await db.SaveChangesAsync(ct);
            var banned = await reader.ExecuteAsync(competition.Id, challenge.Id, userA.Id, now, ct);
            await Assert.That(banned.Single(item => item.Id == paid.Id).Content).IsNull();
            await Assert.That(await reader.ExecuteAsync(Guid.NewGuid(), challenge.Id, userA.Id, now, ct)).IsEmpty();
            challenge.IsPublished = false;
            await db.SaveChangesAsync(ct);
            await Assert.That(await reader.ExecuteAsync(competition.Id, challenge.Id, userA.Id, now, ct)).IsEmpty();
        });
    }

    private static User User(string name, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.test",
        PasswordHash = "test", Kind = UserKind.Human, Role = UserRole.User, CreatedAt = now, UpdatedAt = now
    };

    private static Team Team(Guid userId, Guid competitionId, char token, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), CompetitionId = competitionId, Name = "Team " + token, CaptainId = userId,
        MemberIds = [userId], InvitationToken = new string(token, 32), RegistrationStatus = TeamRegistrationStatus.Approved,
        RegisteredAt = now
    };

    private static CompetitionChallengeHint Hint(long cost, DateTimeOffset? published) => new()
    {
        Id = Guid.NewGuid(), Content = "Hint body " + cost, Cost = cost, PublishedAt = published
    };

    private static GameplayFact Unlock(Guid competitionId, Guid challengeId, Guid teamId, Guid userId, Guid hintId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), CompetitionId = competitionId, CompetitionChallengeId = challengeId, TeamId = teamId,
        ActorUserId = userId, Kind = GameplayFactKind.HintUnlock, State = GameplayFactState.Completed, Result = GameplayFactResult.Unlocked,
        ReferenceKind = GameplayFactReferenceKind.Hint, ReferenceId = hintId, OccurredAt = now, UpdatedAt = now
    };
}
