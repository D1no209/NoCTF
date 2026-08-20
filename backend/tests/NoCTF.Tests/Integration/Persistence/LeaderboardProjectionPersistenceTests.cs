using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NSubstitute;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LeaderboardProjectionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Projection_aggregates_fact_history_in_PostgreSQL_for_every_mode(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_leaderboard_projection")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var commands = new CommandCaptureInterceptor();
            var transactions = new TransactionCaptureInterceptor();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(commands, transactions)
                .Options;
            var projectedAt = DateTimeOffset.UtcNow;

            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            var owner = CreateUser(projectedAt);
            db.Users.Add(owner);

            var fixtures = Enum.GetValues<GameMode>()
                .Select((mode, index) => CreateFixture(mode, index, owner.Id, projectedAt))
                .ToArray();
            db.Competitions.AddRange(fixtures.Select(fixture => fixture.Competition));
            db.Challenges.AddRange(fixtures.Select(fixture => fixture.Challenge));
            db.CompetitionChallenges.AddRange(fixtures.Select(fixture => fixture.CompetitionChallenge));
            db.Teams.AddRange(fixtures.Select(fixture => fixture.Team));
            db.ChallengeFlags.AddRange(fixtures.SelectMany(fixture => fixture.Flags));
            db.GameplayFacts.AddRange(fixtures.SelectMany(fixture => fixture.Facts));
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());

            foreach (var fixture in fixtures)
            {
                commands.Clear();
                var bundle = await cache.CreateBundleAsync(
                    fixture.Competition.Id,
                    projectedAt,
                    cancellationToken);

                await Assert.That(bundle).IsNotNull();
                await Assert.That(bundle!.Scoreboard.Snapshot.Teams).HasSingleItem();
                await Assert.That(bundle.Scoreboard.Snapshot.Teams[0].Slots).IsNotEmpty();
                var expected = CreateExpectedScoreboard(fixture, projectedAt);
                var actualRow = bundle.Scoreboard.Snapshot.Teams[0];
                var expectedRow = expected.Snapshot.Teams.Single();
                await Assert.That(actualRow.TotalScore).IsEqualTo(expectedRow.TotalScore);
                await Assert.That(actualRow.Slots.Count).IsEqualTo(expectedRow.Slots.Count);
                var actualSlots = actualRow.Slots.OrderBy(slot => slot.ColumnIndex).ToArray();
                var expectedSlots = expectedRow.Slots.OrderBy(slot => slot.ColumnIndex).ToArray();
                for (var slotIndex = 0; slotIndex < expectedSlots.Length; slotIndex++)
                {
                    var actual = actualSlots[slotIndex];
                    var expectedSlot = expectedSlots[slotIndex];
                    await Assert.That(actual.ColumnIndex).IsEqualTo(expectedSlot.ColumnIndex);
                    await Assert.That(actual.ScoreState).IsEqualTo(expectedSlot.ScoreState);
                    await Assert.That(actual.EarnedPoints).IsEqualTo(expectedSlot.EarnedPoints);
                    await Assert.That(actual.DeductedPoints).IsEqualTo(expectedSlot.DeductedPoints);
                    await Assert.That(actual.NetPoints).IsEqualTo(expectedSlot.NetPoints);
                    await Assert.That(actual.EntryCount).IsEqualTo(expectedSlot.EntryCount);

                    var actualBreakdowns = actual.Breakdowns.OrderBy(item => item.Kind).ToArray();
                    var expectedBreakdowns = expectedSlot.Breakdowns.OrderBy(item => item.Kind).ToArray();
                    await Assert.That(actualBreakdowns).IsEquivalentTo(expectedBreakdowns);
                }
                var entryCount = bundle.Scoreboard.Snapshot.Teams[0].Slots.Sum(slot => slot.EntryCount);
                if (fixture.Competition.Mode == GameMode.Koh)
                    await Assert.That(entryCount).IsEqualTo(249);
                else
                    await Assert.That(entryCount).IsGreaterThanOrEqualTo(250);

                var factQueries = commands.Commands
                    .Where(command => command.Contains("gameplay_facts", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                await Assert.That(factQueries.Length).IsGreaterThanOrEqualTo(1);
                await Assert.That(factQueries.Length).IsLessThanOrEqualTo(2);
                await Assert.That(factQueries.Any(command =>
                        command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)))
                    .IsTrue();
            }

            var ctf = fixtures.Single(fixture => fixture.Competition.Mode == GameMode.Ctf);
            var futureFact = new GameplayFact
            {
                Id = Guid.CreateVersion7(projectedAt.AddMinutes(1)),
                CompetitionId = ctf.Competition.Id,
                CompetitionChallengeId = ctf.CompetitionChallenge.Id,
                TeamId = ctf.Team.Id,
                ActorUserId = owner.Id,
                Kind = GameplayFactKind.FlagAttempt,
                OccurredAt = projectedAt.AddMinutes(1),
                Value = "flag{future}",
                ValueSha256 = new byte[32],
                State = GameplayFactState.Completed,
                Result = GameplayFactResult.Correct,
                UpdatedAt = projectedAt.AddMinutes(1)
            };
            db.GameplayFacts.Add(futureFact);
            await db.SaveChangesAsync(cancellationToken);
            var historical = await cache.CreateBundleAsync(
                ctf.Competition.Id,
                projectedAt,
                cancellationToken);

            await Assert.That(historical!.Scoreboard.EntryAllocations
                    .Select(item => item.Entry.Id))
                .DoesNotContain(futureFact.Id);
            await Assert.That(historical.Scoreboard.DetailActors
                    .Where(item => item.UserId == owner.Id)
                    .Select(item => item.DisplayName)
                    .Distinct())
                .IsEquivalentTo([owner.UserName]);

            await cache.RefreshAsync(ctf.Competition.Id, cancellationToken);
            await Assert.That(transactions.IsolationLevels)
                .Contains(IsolationLevel.RepeatableRead);
        });
    }

    private static ScoreboardProjection CreateExpectedScoreboard(Fixture fixture, DateTimeOffset projectedAt)
    {
        var team = fixture.Team;
        var challenge = fixture.CompetitionChallenge;
        var facts = fixture.Facts.Select(fact => new LeaderboardGameplayFact(
            fact.Id,
            fact.TeamId,
            fact.CompetitionChallengeId,
            fact.Kind,
            fact.OccurredAt,
            fact.State,
            fact.Result,
            fact.FailureCode,
            fact.ReferenceKind,
            fact.ReferenceId,
            fact.VictimTeamId,
            null,
            fact.Value,
            null,
            fact.ActorUserId)).ToArray();
        var rounds = fixture.Flags.Select(flag => new LeaderboardAwdRoundFact(
            flag.CompetitionChallengeId!.Value,
            flag.TeamId!.Value,
            flag.SpecificationId!.Value,
            flag.ValidStart!.Value,
            flag.ValidUntil!.Value)).ToArray();
        return new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).ProjectScoreboard(new(
            fixture.Competition.Id,
            fixture.Competition.Mode,
            [new LeaderboardTeamFact(team.Id, team.Name, false, false, team.RegisteredAt)],
            facts,
            [new LeaderboardChallengeFact(
                challenge.Id,
                fixture.Challenge.Direction,
                fixture.Challenge.Title,
                false,
                challenge.RulesJson,
                challenge.Order,
                challenge.IsPublished,
                challenge.Revision)],
            fixture.Competition.ConfigurationJson,
            fixture.Competition.StartAt,
            AwdRounds: rounds,
            ProjectedAt: projectedAt,
            CompetitionStatus: fixture.Competition.Status));
    }

    private static Fixture CreateFixture(
        GameMode mode,
        int index,
        Guid ownerId,
        DateTimeOffset projectedAt)
    {
        var competitionId = Guid.CreateVersion7(projectedAt.AddMinutes(index));
        var challengeId = Guid.CreateVersion7(projectedAt.AddMinutes(index + 10));
        var competitionChallengeId = Guid.CreateVersion7(projectedAt.AddMinutes(index + 20));
        var teamId = Guid.CreateVersion7(projectedAt.AddMinutes(index + 30));
        var start = projectedAt.AddMinutes(-20);
        var rules = new GameModeChallengeConfigurationCatalog().GetDefaultJson(mode);
        var competition = new Competition
        {
            Id = competitionId,
            Title = $"{mode} projection",
            OwnerId = ownerId,
            Mode = mode,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(mode),
            ConfigurationRevision = 1,
            ConfigurationUpdatedAt = start,
            TrackConfigurationUpdatedAt = start,
            FlagDerivationSecret = new byte[32],
            StartAt = start,
            EndAt = projectedAt.AddHours(1),
            RunningSince = start,
            Status = CompetitionStatus.Running,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            CreatedAt = start,
            UpdatedAt = start
        };
        var challenge = new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = mode,
            Visibility = ChallengeVisibility.Private,
            Title = $"{mode} challenge",
            Direction = "Pwn",
            DefinitionJson = rules,
            Revision = 1,
            CreatedAt = start,
            UpdatedAt = start
        };
        var competitionChallenge = new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 500,
            Order = 1,
            IsPublished = true,
            RulesJson = rules,
            Revision = 1,
            UpdatedAt = start
        };
        var team = new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = $"{mode} team",
            NormalizedName = $"{mode.ToString().ToUpperInvariant()} TEAM",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = $"{index:D2}".PadRight(32, 'a'),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = start
        };
        var kind = mode switch
        {
            GameMode.Ctf => GameplayFactKind.FlagAttempt,
            GameMode.Awd => GameplayFactKind.FlagAttempt,
            GameMode.Awdp => GameplayFactKind.BreakAttempt,
            GameMode.Koh => GameplayFactKind.KohControlObservation,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        var result = mode == GameMode.Koh
            ? GameplayFactResult.Controlled
            : GameplayFactResult.Wrong;
        var roundId = Guid.CreateVersion7(start);
        var facts = Enumerable.Range(0, 250)
            .Select(sequence => new GameplayFact
            {
                Id = Guid.CreateVersion7(start.AddSeconds(sequence + 1)),
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = mode == GameMode.Koh && sequence == 100 ? null : teamId,
                ActorUserId = mode == GameMode.Koh ? null : ownerId,
                Kind = kind,
                OccurredAt = start.AddSeconds(sequence + 1),
                ReferenceKind = mode == GameMode.Awd ? GameplayFactReferenceKind.AwdRound : null,
                ReferenceId = mode == GameMode.Awd ? roundId : null,
                Value = mode == GameMode.Koh ? null : $"flag{{{sequence:D8}}}",
                ValueSha256 = mode == GameMode.Koh ? null : new byte[32],
                State = GameplayFactState.Completed,
                Result = mode == GameMode.Koh && sequence == 100
                    ? GameplayFactResult.Uncontrolled
                    : result,
                UpdatedAt = start.AddSeconds(sequence + 1)
            })
            .ToArray();
        IReadOnlyList<ChallengeFlag> flags = mode == GameMode.Awd
            ?
            [
                new ChallengeFlag
                {
                    Id = Guid.CreateVersion7(start.AddMinutes(1)),
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = teamId,
                    Flag = "flag{round}",
                    FlagSha256 = new byte[32],
                    SpecificationKind = SpecificationKind.AwdRound,
                    SpecificationId = roundId,
                    ValidStart = start,
                    ValidUntil = start.AddMinutes(5),
                    CreatedAt = start
                }
            ]
            : [];
        return new(competition, challenge, competitionChallenge, team, facts, flags);
    }

    private static User CreateUser(DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        UserName = "administrator",
        NormalizedUserName = "ADMINISTRATOR",
        Email = "administrator@example.test",
        NormalizedEmail = "ADMINISTRATOR@EXAMPLE.TEST",
        PasswordHash = "test",
        Role = UserRole.Administrator,
        CreatedAt = now,
        UpdatedAt = now
    };

    private sealed record Fixture(
        Competition Competition,
        Challenge Challenge,
        CompetitionChallenge CompetitionChallenge,
        Team Team,
        IReadOnlyList<GameplayFact> Facts,
        IReadOnlyList<ChallengeFlag> Flags);

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        private readonly List<string> commands = [];

        public IReadOnlyList<string> Commands => commands;

        public void Clear() => commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransactionCaptureInterceptor : DbTransactionInterceptor
    {
        private readonly List<IsolationLevel> isolationLevels = [];

        public IReadOnlyList<IsolationLevel> IsolationLevels => isolationLevels;

        public override DbTransaction TransactionStarted(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result)
        {
            isolationLevels.Add(result.IsolationLevel);
            return result;
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            isolationLevels.Add(result.IsolationLevel);
            return ValueTask.FromResult(result);
        }
    }
}
