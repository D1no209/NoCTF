using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Hints;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class HintUnlockLifecyclePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Hint_unlock_requires_running_at_intake_and_async_adjudication(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_hint_unlock_lifecycle")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var hintId = Guid.CreateVersion7();
            var teamId = Guid.CreateVersion7();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(cancellationToken);
                setup.Users.Add(new User
                {
                    Id = userId,
                    UserName = "hint-player",
                    NormalizedUserName = "HINT-PLAYER",
                    Email = "hint-player@example.test",
                    PasswordHash = "test",
                    Kind = UserKind.Human,
                    Role = UserRole.User,
                    EmailVerifiedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.Competitions.Add(new CtfCompetition
                {
                    Id = competitionId,
                    OwnerId = userId,
                    Title = "Hint lifecycle",
                    ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddMinutes(-5),
                    EndAt = now.AddHours(1),
                    Status = CompetitionStatus.Draft,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.Challenges.Add(new CtfChallenge
                {
                    Id = challengeId,
                    OwnerId = userId,
                    Visibility = ChallengeVisibility.Private,
                    Title = "Hint challenge",
                    Direction = "Web",
                    Definition = TestConfigurations.Definition(GameMode.Ctf),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.CompetitionChallenges.Add(new CtfCompetitionChallenge
                {
                    Id = competitionChallengeId,
                    CompetitionId = competitionId,
                    ChallengeId = challengeId,
                    IsPublished = true,
                    Rules = TestConfigurations.Rules(GameMode.Ctf),
                    UpdatedAt = now,
                    Hints =
                    [
                        new CompetitionChallengeHint
                        {
                            Id = hintId,
                            Content = "Lifecycle protected hint",
                            Cost = 0,
                            PublishedAt = now.AddMinutes(-1)
                        }
                    ]
                });
                setup.Teams.Add(new Team
                {
                    Id = teamId,
                    CompetitionId = competitionId,
                    Name = "Hint Team",
                    CaptainId = userId,
                    MemberIds = [userId],
                    InvitationToken = new string('h', 32),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                });
                await setup.SaveChangesAsync(cancellationToken);
            }

            foreach (var status in Enum.GetValues<CompetitionStatus>()
                         .Where(status => status != CompetitionStatus.Running))
            {
                await using var notRunningDb = new NoCtfDbContext(options);
                await notRunningDb.Competitions
                    .Where(competition => competition.Id == competitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        competition => competition.Status,
                        status), cancellationToken);
                var attempt = await new ChallengeHintStore(
                        notRunningDb,
                        Substitute.For<IPostCommitMessagePublisher>())
                    .UnlockAsync(
                        competitionId,
                        competitionChallengeId,
                        hintId,
                        userId,
                        now,
                        cancellationToken);
                await Assert.That(attempt.Failure).IsEqualTo(
                    NoCTF.Application.Challenges.Hints.HintUnlockFailure.NotFound);
                await Assert.That(await notRunningDb.GameplayFacts.CountAsync(cancellationToken))
                    .IsEqualTo(0);
            }

            Guid gameplayFactId;
            await using (var runningDb = new NoCtfDbContext(options))
            {
                await runningDb.Competitions
                    .Where(competition => competition.Id == competitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        competition => competition.Status,
                        CompetitionStatus.Running), cancellationToken);
                var attempt = await new ChallengeHintStore(
                        runningDb,
                        Substitute.For<IPostCommitMessagePublisher>())
                    .UnlockAsync(
                        competitionId,
                        competitionChallengeId,
                        hintId,
                        userId,
                        now,
                        cancellationToken);
                await Assert.That(attempt.Failure).IsNull();
                await Assert.That(attempt.Result!.Created).IsTrue();
                gameplayFactId = attempt.Result.GameplayFactId;
            }

            await using (var pauseDb = new NoCtfDbContext(options))
            {
                await pauseDb.Competitions
                    .Where(competition => competition.Id == competitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        competition => competition.Status,
                        CompetitionStatus.Paused), cancellationToken);
            }

            await using (var processingDb = new NoCtfDbContext(options))
            {
                var admissionPolicy = Substitute.For<IGameplayFactAdmissionModePolicy>();
                admissionPolicy.GetRules(
                        Arg.Any<GameMode>(),
                        Arg.Any<CompetitionModeConfiguration>(),
                        Arg.Any<CompetitionChallengeRules>(),
                        Arg.Any<ChallengeDefinition?>())
                    .Returns(new GameplayFactAdmissionRules(false, false, null, null));
                var processor = new GameplayFactProcessor(
                    processingDb,
                    Substitute.For<IGameplayFactEvaluatorCatalog>(),
                    admissionPolicy,
                    Substitute.For<IPostCommitMessagePublisher>(),
                    Substitute.For<ILeaderboardSnapshotFactory>());

                await processor.ProcessAsync(gameplayFactId, cancellationToken);
            }

            await using var verification = new NoCtfDbContext(options);
            var fact = await verification.GameplayFacts.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == gameplayFactId, cancellationToken);
            await Assert.That(fact.State).IsEqualTo(GameplayFactState.Completed);
            await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.Rejected);
            await Assert.That(fact.FailureCode).IsEqualTo(GameplayFactFailureCode.HintUnavailable);
        });
    }
}
