using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Checking;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class LeaderboardProjectionPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Awd_scoring_preview_expires_when_time_alone_changes_confirmed_impact(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var start = DateTimeOffset.UtcNow;
        var owner = CreateUser(start); var fixture = CreateFixture(GameMode.Awd, 0, owner.Id, start);
        fixture.Competition.StartAt = start; fixture.Competition.EndAt = start.AddSeconds(360);
        var configuration = (AwdCompetitionModeConfiguration)fixture.Competition.ModeConfiguration!;
        configuration.HardeningDurationSeconds = 0; configuration.RoundDurationSeconds = 180;
        db.Users.Add(owner); db.Competitions.Add(fixture.Competition); db.Challenges.Add(fixture.Challenge);
        db.CompetitionChallenges.Add(fixture.CompetitionChallenge); db.Teams.Add(fixture.Team);
        db.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
            PreviousCompetitionStatus = CompetitionStatus.Published, CompetitionStatus = CompetitionStatus.Running, OccurredAt = start });
        var round = new AwdRound { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, Number = 1,
            StartsAt = start, EndsAt = start.AddSeconds(180), ActiveEndTicks = TimeSpan.FromSeconds(180).Ticks };
        var rule = new AwdRoundRules { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
            CompetitionChallengeId = fixture.CompetitionChallenge.Id, RoundId = round.Id, Revision = 1, EffectiveAt = start,
            AttackRewardMode = AwdAttackRewardMode.FixedPerAttack, AttackPoints = 50, VictimDefensePoolPoints = 100,
            ServiceHealthyPoints = 0, CheckerIntervalSeconds = 30, EligibleTeamCount = 1,
            Stores = [new() { StoreId = Guid.NewGuid(), Weight = 1, FlagsPerIssue = 1 }],
            Teams = [new() { TeamId = fixture.Team.Id }], Options = new() };
        db.AwdRounds.Add(round); db.AwdRoundRules.Add(rule);
        db.AwdFlagKeys.Add(new() { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, Version = 1, ProtectedSecret = [1], CreatedAt = start });
        var flagId = Guid.NewGuid(); var digest = System.Security.Cryptography.SHA256.HashData("clock-flag"u8);
        db.ChallengeFlags.Add(new AwdRoundChallengeFlag { Id = flagId, CompetitionChallengeId = fixture.CompetitionChallenge.Id,
            TeamId = fixture.Team.Id, Flag = "clock-flag", FlagSha256 = digest, CreatedAt = start, ValidStart = start,
            SpecificationKind = SpecificationKind.AwdRound, SpecificationId = AwdRoundSpecificationId.FromRound(1).Value,
            ValidUntil = start.AddSeconds(100),
            AwdIssue = new() { FlagId = flagId, CompetitionId = fixture.Competition.Id, CompetitionChallengeId = fixture.CompetitionChallenge.Id,
                TeamId = fixture.Team.Id, RoundId = round.Id, RoundNumber = 1, RulesId = rule.Id, StoreId = rule.Stores[0].StoreId,
                State = AwdFlagIssueState.Issued, IssuedAt = start, Digest = Convert.ToHexString(digest),
                ActiveExpiryTicks = TimeSpan.FromSeconds(100).Ticks, HistoricalActiveExpiryTicks = TimeSpan.FromSeconds(100).Ticks } });
        foreach (var sample in AwdCheckPlan.Create(round, rule, 0).Where(sample => sample.ActiveTicks < TimeSpan.FromSeconds(100).Ticks))
        {
            var id = Guid.NewGuid(); var observed = start.AddTicks(sample.ActiveTicks).AddSeconds(1);
            db.GameplayFacts.Add(new AwdServiceTransitionGameplayFact { Id = id, CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = fixture.CompetitionChallenge.Id, TeamId = fixture.Team.Id, ReferenceKind = GameplayFactReferenceKind.AwdRound,
                ReferenceId = round.Id, OccurredAt = observed, UpdatedAt = observed.AddSeconds(1), State = GameplayFactState.Completed,
                Result = GameplayFactResult.ServiceUp, Awd = new() { GameplayFactId = id, RulesId = rule.Id,
                    SampleActiveTicks = sample.ActiveTicks, SampleDeadlineActiveTicks = sample.DeadlineActiveTicks,
                    Health = AwdHealth.Ok, ManifestComplete = true, Settlement = AwdSettlementState.Final,
                    Execution = AwdCheckExecution.Completed, ObservedAt = observed, ReceivedAt = observed.AddSeconds(1),
                    Flags = [new() { GameplayFactId = id, FlagId = flagId, Read = AwdProbeOutcome.Passed, Obligation = AwdFlagObligation.PlayerRequired }] } });
        }
        await db.SaveChangesAsync(ct);
        using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
        var projections = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>());
        var publisher = Substitute.For<IPostCommitMessagePublisher>();
        var secrets = new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions
            { EncryptionKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) }));
        var store = new AwdScoringRuleStore(db, projections, secrets, new CompetitionEventStore(db, publisher), publisher);
        var candidate = new AwdScoringRuleCandidate(fixture.Competition.Id, fixture.CompetitionChallenge.Id, 1, 1,
            AwdRuleChangeTiming.Immediately, AwdScoringPolicy.From(rule) with { ConfidentialityPoints = 100 }, owner.Id,
            "Review confidentiality reward", start.AddSeconds(95));
        var original = (await store.PreviewAsync(candidate, ct)).Preview!;
        await Assert.That(original.Teams.Single().ScoreAfter).IsEqualTo(0L);
        var later = candidate with { Now = start.AddSeconds(105) };
        var updated = (await store.PreviewAsync(later, ct)).Preview!;
        await Assert.That(updated.Teams.Single().ScoreAfter).IsEqualTo(100L);
        await Assert.That((await store.ApplyAsync(later, original.Token, ct)).Failure).IsEqualTo(AwdScoringRuleFailure.PreviewExpired);
        await Assert.That(await db.Set<AwdScoringRuleChange>().CountAsync(ct)).IsEqualTo(0);
        await Assert.That((await store.ApplyAsync(later with { Now = start.AddSeconds(106) }, updated.Token, ct)).Failure).IsNull();
    });
}
