using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class FlagAttemptStateReaderPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Four_modes_project_attempt_state_without_loading_definition_graph(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_attempt_state_modes")
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
            var scopes = new List<(GameMode Mode, Guid CompetitionId, Guid ChallengeId, Guid TeamId)>();
            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync(cancellationToken);
                seed.Users.Add(new User
                {
                    Id = userId,
                    UserName = "attempt-reader",
                    NormalizedUserName = "ATTEMPT-READER",
                    Email = "attempt-reader@example.test",
                    PasswordHash = "test",
                    Kind = UserKind.Human,
                    Role = UserRole.User,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                foreach (var mode in Enum.GetValues<GameMode>())
                {
                    var competitionId = Guid.CreateVersion7();
                    var templateId = Guid.CreateVersion7();
                    var challengeId = Guid.CreateVersion7();
                    var teamId = Guid.CreateVersion7();
                    Competition competition = mode switch
                    {
                        GameMode.Ctf => new CtfCompetition(),
                        GameMode.Awd => new AwdCompetition(),
                        GameMode.Awdp => new AwdpCompetition(),
                        GameMode.Koh => new KohCompetition(),
                        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
                    };
                    competition.Id = competitionId;
                    competition.OwnerId = userId;
                    competition.Title = $"{mode} attempts";
                    competition.NormalizedTitle = $"{mode}_ATTEMPTS";
                    competition.Status = CompetitionStatus.Running;
                    competition.StartAt = now.AddHours(-1);
                    competition.EndAt = now.AddHours(1);
                    competition.FlagDerivationSecret = new byte[32];
                    competition.ModeConfiguration = TestConfigurations.Competition(mode);
                    competition.CreatedAt = now;
                    competition.UpdatedAt = now;
                    seed.Competitions.Add(competition);

                    Challenge template = mode switch
                    {
                        GameMode.Ctf => new CtfChallenge(),
                        GameMode.Awd => new AwdChallenge(),
                        GameMode.Awdp => new AwdpChallenge(),
                        GameMode.Koh => new KohChallenge(),
                        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
                    };
                    template.Id = templateId;
                    template.OwnerId = userId;
                    template.Title = $"{mode} template";
                    template.NormalizedTitle = $"{mode}_TEMPLATE";
                    template.Definition = TestConfigurations.Definition(mode);
                    template.CreatedAt = now;
                    template.UpdatedAt = now;
                    seed.Challenges.Add(template);

                    CompetitionChallenge challenge = mode switch
                    {
                        GameMode.Ctf => new CtfCompetitionChallenge(),
                        GameMode.Awd => new AwdCompetitionChallenge(),
                        GameMode.Awdp => new AwdpCompetitionChallenge(),
                        GameMode.Koh => new KohCompetitionChallenge(),
                        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
                    };
                    challenge.Id = challengeId;
                    challenge.CompetitionId = competitionId;
                    challenge.ChallengeId = templateId;
                    challenge.IsPublished = true;
                    challenge.Rules = TestConfigurations.Rules(mode);
                    challenge.UpdatedAt = now;
                    seed.CompetitionChallenges.Add(challenge);
                    seed.Teams.Add(new Team
                    {
                        Id = teamId,
                        CompetitionId = competitionId,
                        Name = $"{mode} team",
                        NormalizedName = $"{mode}_TEAM",
                        CaptainId = userId,
                        MemberIds = [userId],
                        InvitationToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
                        RegistrationStatus = TeamRegistrationStatus.Approved,
                        RegisteredAt = now
                    });
                    scopes.Add((mode, competitionId, challengeId, teamId));
                }
                await seed.SaveChangesAsync(cancellationToken);
                foreach (var scope in scopes.Where(item => item.Mode != GameMode.Koh))
                {
                    var fact = scope.Mode == GameMode.Awdp
                        ? (GameplayFact)new BreakAttemptGameplayFact()
                        : new FlagAttemptGameplayFact();
                    fact.Id = Guid.CreateVersion7();
                    fact.CompetitionId = scope.CompetitionId;
                    fact.CompetitionChallengeId = scope.ChallengeId;
                    fact.TeamId = scope.TeamId;
                    fact.ActorUserId = userId;
                    fact.Value = "flag{attempt-state}";
                    fact.ValueSha256 = System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(fact.Value));
                    fact.OccurredAt = now;
                    fact.UpdatedAt = now;
                    fact.State = GameplayFactState.Completed;
                    fact.Result = scope.Mode == GameMode.Ctf
                        ? GameplayFactResult.Correct : GameplayFactResult.Wrong;
                    seed.GameplayFacts.Add(fact);
                }
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using var read = new NoCtfDbContext(options);
            var reader = new FlagAttemptStateReader(read);
            foreach (var scope in scopes)
            {
                var state = await reader.ReadAsync(scope.CompetitionId, scope.ChallengeId,
                    scope.TeamId, scope.Mode, CompetitionStatus.Running, cancellationToken);
                var expectedCount = scope.Mode == GameMode.Koh ? 0 : 1;
                await Assert.That(state?.Accepted).IsEqualTo(expectedCount);
                await Assert.That(state?.Solved).IsEqualTo(scope.Mode == GameMode.Ctf);
                await Assert.That(state?.Maximum).IsEqualTo(scope.Mode == GameMode.Awdp ? 10 : null);
                await Assert.That(state?.Remaining).IsEqualTo(scope.Mode == GameMode.Awdp ? 9 : null);
            }
        });
    }
}
