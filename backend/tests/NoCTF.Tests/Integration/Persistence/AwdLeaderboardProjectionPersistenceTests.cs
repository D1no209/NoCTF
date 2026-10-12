using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.Competitions.Awd;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class LeaderboardProjectionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Awd_projection_uses_immutable_rules_capture_identities_and_complete_check_evidence(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_projection").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var start = now.AddHours(-2);
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Awd, 0, owner.Id, now);
            fixture.Competition.StartAt = start;
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            var attackers = Enumerable.Range(1, 2).Select(index =>
            {
                var user = CreateUser(now.AddTicks(index));
                user.UserName = user.NormalizedUserName = $"attacker-{index}";
                user.Email = $"attacker-{index}@example.test";
                db.Users.Add(user);
                var team = new Team { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, Name = user.UserName,
                    CaptainId = user.Id, MemberIds = [user.Id], InvitationToken = Guid.NewGuid().ToString("N"),
                    RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = start };
                db.Teams.Add(team);
                return team;
            }).ToArray();
            db.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent { Id = Guid.NewGuid(),
                CompetitionId = fixture.Competition.Id, PreviousCompetitionStatus = CompetitionStatus.Published,
                CompetitionStatus = CompetitionStatus.Running, OccurredAt = start });
            var rounds = Enumerable.Range(1, 60).Select(number => new AwdRound { Id = Guid.NewGuid(),
                CompetitionId = fixture.Competition.Id, Number = number, StartsAt = start.AddMinutes(number - 1), EndsAt = start.AddMinutes(number),
                ActiveStartTicks = TimeSpan.FromMinutes(number - 1).Ticks, ActiveEndTicks = TimeSpan.FromMinutes(number).Ticks }).ToArray();
            db.AwdRounds.AddRange(rounds);
            var snapshots = rounds.Select(round => new AwdRoundRules { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = fixture.CompetitionChallenge.Id, RoundId = round.Id, Revision = 1, EffectiveAt = round.StartsAt,
                AttackRewardMode = AwdAttackRewardMode.SplitVictimDefensePool, AttackPoints = 50, VictimDefensePoolPoints = 100,
                ServiceHealthyPoints = 100, CheckerIntervalSeconds = 30, EligibleTeamCount = 3,
                Options = new() { DefenseMode = AwdDefenseMode.SharedPool, AttackSettlement = AwdAttackSettlement.FlagExpiry },
                Stores = [new() { StoreId = Guid.NewGuid(), Weight = 1, FlagsPerIssue = 3 }],
                Teams = attackers.Select(team => new AwdRoundTeamEligibility { TeamId = team.Id })
                    .Append(new() { TeamId = fixture.Team.Id }).ToList() }).ToArray();
            db.AwdRoundRules.AddRange(snapshots);
            foreach (var round in rounds)
            {
                var id = Guid.NewGuid();
                db.GameplayFacts.Add(new AwdServiceTransitionGameplayFact { Id = id, CompetitionId = fixture.Competition.Id,
                    CompetitionChallengeId = fixture.CompetitionChallenge.Id, TeamId = fixture.Team.Id,
                    ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = round.Id,
                    OccurredAt = round.EndsAt.AddSeconds(-30), UpdatedAt = round.EndsAt.AddSeconds(-5),
                    State = GameplayFactState.Completed, Result = GameplayFactResult.ServiceUp,
                    Awd = new() { GameplayFactId = id, RulesId = snapshots[round.Number - 1].Id,
                        SampleActiveTicks = round.ActiveEndTicks - TimeSpan.FromSeconds(30).Ticks,
                        FinalScheduledCheck = true, ManifestComplete = true, Health = AwdHealth.Ok,
                        Settlement = AwdSettlementState.Final, Execution = AwdCheckExecution.Completed,
                        ObservedAt = round.EndsAt.AddSeconds(-5), ReceivedAt = round.EndsAt.AddSeconds(-4) } });
            }
            await db.SaveChangesAsync(ct);
            using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
            var cache = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>());
            var initial = (await cache.CreateScoreboardAsync(fixture.Competition.Id, now, ct))!;
            var defender = initial.Snapshot.Teams.Single(x => x.TeamId == fixture.Team.Id);
            await Assert.That(defender.TotalScore).IsEqualTo(6000);
            await Assert.That(defender.ScoreOutsideWindow).IsEqualTo(1000);
            await Assert.That(initial.Schema.Rounds.Select(x => x.Number)).IsEquivalentTo(Enumerable.Range(11, 50));
            await Assert.That(defender.Slots.All(x => x.AwdComponents.Single(c => c.Component == AwdScoreComponent.Availability).Settlement == AwdSettlementState.Final)).IsTrue();
            await Assert.That(initial.Snapshot.Teams.Single(x => x.TeamId == attackers[0].Id).Slots
                .All(x => x.AwdComponents.Single(c => c.Component == AwdScoreComponent.Availability).Points == null)).IsTrue();

            var eligible = attackers.Select(x => x.Id).Append(fixture.Team.Id).ToArray();
            await Assert.That(await AwdSettledRankingReader.ReadAsync(db, fixture.Competition, 61, eligible, now, ct)).IsEmpty();
            foreach (var attacker in attackers)
            {
                var id = Guid.NewGuid();
                db.GameplayFacts.Add(new AwdServiceTransitionGameplayFact { Id = id, CompetitionId = fixture.Competition.Id,
                    CompetitionChallengeId = fixture.CompetitionChallenge.Id, TeamId = attacker.Id,
                    ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = rounds[0].Id,
                    OccurredAt = start.AddSeconds(30), UpdatedAt = start.AddSeconds(55),
                    State = GameplayFactState.Completed, Result = GameplayFactResult.ServiceDown,
                    Awd = new() { GameplayFactId = id, RulesId = snapshots[0].Id,
                        SampleActiveTicks = TimeSpan.FromSeconds(30).Ticks, FinalScheduledCheck = true, ManifestComplete = true,
                        Health = AwdHealth.Offline, Settlement = AwdSettlementState.Final, Execution = AwdCheckExecution.Completed,
                        ObservedAt = start.AddSeconds(55), ReceivedAt = start.AddSeconds(56) } });
            }
            await db.SaveChangesAsync(ct);
            var ranks = await AwdSettledRankingReader.ReadAsync(db, fixture.Competition, 61, eligible, now, ct);
            await Assert.That(ranks[fixture.Team.Id]).IsEqualTo(1);
            await Assert.That(ranks.Values).IsEquivalentTo([1, 2, 3]);

            var firstRule = snapshots[0];
            db.AwdFlagKeys.Add(new() { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
                Version = 1, ProtectedSecret = [1], CreatedAt = start });
            for (var ordinal = 0; ordinal < 3; ordinal++)
            {
                var flagId = Guid.NewGuid();
                var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"flag{{{flagId:N}}}"));
                db.ChallengeFlags.Add(new AwdRoundChallengeFlag { Id = flagId, CompetitionChallengeId = fixture.CompetitionChallenge.Id,
                    TeamId = fixture.Team.Id, Flag = $"flag{{{flagId:N}}}", FlagSha256 = digest, CreatedAt = start,
                    SpecificationKind = SpecificationKind.AwdRound, SpecificationId = AwdRoundSpecificationId.FromRound(1).Value,
                    ValidStart = start, ValidUntil = start.AddMinutes(5),
                    AwdIssue = new() { FlagId = flagId, CompetitionId = fixture.Competition.Id,
                        CompetitionChallengeId = fixture.CompetitionChallenge.Id, TeamId = fixture.Team.Id,
                        RoundId = rounds[0].Id, RoundNumber = 1, RulesId = firstRule.Id, StoreId = firstRule.Stores[0].StoreId,
                        Ordinal = ordinal, State = AwdFlagIssueState.Issued, IssuedAt = start,
                        ExpiryRound = 6, HistoricalExpiryRound = 6, Digest = Convert.ToHexString(digest) } });
                foreach (var attacker in attackers)
                {
                    var factId = Guid.NewGuid();
                    var received = start.AddMinutes(2).AddSeconds(10 + ordinal);
                    db.GameplayFacts.Add(new FlagAttemptGameplayFact { Id = factId, CompetitionId = fixture.Competition.Id,
                        CompetitionChallengeId = fixture.CompetitionChallenge.Id, TeamId = attacker.Id, VictimTeamId = fixture.Team.Id,
                        OccurredAt = received, UpdatedAt = received, State = GameplayFactState.Completed, Result = GameplayFactResult.Correct,
                        ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = rounds[0].Id,
                        Value = $"flag{{{flagId:N}}}", ValueSha256 = new byte[32],
                        Awd = new() { GameplayFactId = factId, FlagId = flagId, RulesId = firstRule.Id, GenerationRoundId = rounds[0].Id,
                            ReceivedRoundId = rounds[2].Id, ReceivedRoundNumber = 3, ReceiptScorable = true, AttackOutcome = AwdAttackOutcome.Accepted } });
                    db.AwdCaptureIdentities.Add(new() { CompetitionId = fixture.Competition.Id, FlagId = flagId,
                        AttackingTeamId = attacker.Id, GameplayFactId = factId });
                }
            }
            // Editing live defaults cannot rewrite the rules already attached to sixty completed rounds.
            ((AwdCompetitionModeConfiguration)fixture.Competition.ModeConfiguration!).ServiceHealthyPoints = 0;
            await db.SaveChangesAsync(ct);
            var scored = (await cache.CreateScoreboardWindowAsync(fixture.Competition.Id, 50, now, ct))!;
            defender = scored.Snapshot.Teams.Single(x => x.TeamId == fixture.Team.Id);
            await Assert.That(defender.TotalScore).IsEqualTo(5900);
            foreach (var attacker in attackers)
            {
                var row = scored.Snapshot.Teams.Single(x => x.TeamId == attacker.Id);
                await Assert.That(row.TotalScore).IsEqualTo(50);
                await Assert.That(row.AttackScore).IsEqualTo(50L);
                var generation = row.Slots.Single(slot => scored.Schema.Columns[slot.ColumnIndex].RoundId == rounds[0].Id);
                await Assert.That(generation.AwdComponents.Single(x => x.Component == AwdScoreComponent.Attack).Points).IsEqualTo(50L);
                await Assert.That(generation.Breakdowns.Single(x => x.Kind == ScoreboardBreakdownKind.Attack).AttemptCount).IsEqualTo(0);
                await Assert.That(generation.Breakdowns.Single(x => x.Kind == ScoreboardBreakdownKind.Attack).SuccessfulCount).IsEqualTo(0);
                var receipt = row.Slots.Single(slot => scored.Schema.Columns[slot.ColumnIndex].RoundId == rounds[2].Id);
                await Assert.That(receipt.Entries.Count(x => x.Kind == ScoreboardEntryKind.Attack)).IsEqualTo(3);
                await Assert.That(receipt.Breakdowns.Single(x => x.Kind == ScoreboardBreakdownKind.Attack).AttemptCount).IsEqualTo(3);
                await Assert.That(receipt.Breakdowns.Single(x => x.Kind == ScoreboardBreakdownKind.Attack).SuccessfulCount).IsEqualTo(3);
                await Assert.That(row.ScoreOutsideWindow + row.Slots.Sum(x => x.NetPoints.GetValueOrDefault())).IsEqualTo(row.TotalScore);
            }
            fixture.CompetitionChallenge.ScoringEndsAt = start.AddMinutes(2).AddSeconds(5);
            await db.SaveChangesAsync(ct);
            var narrowed = (await cache.CreateScoreboardWindowAsync(fixture.Competition.Id, 50, now, ct))!;
            await Assert.That(narrowed.Snapshot.Teams.Single(x => x.TeamId == fixture.Team.Id).TotalScore).IsEqualTo(200);
            await Assert.That(narrowed.Snapshot.Teams.Where(x => x.TeamId != fixture.Team.Id).All(x => x.TotalScore == 0)).IsTrue();
        });
    }
}
