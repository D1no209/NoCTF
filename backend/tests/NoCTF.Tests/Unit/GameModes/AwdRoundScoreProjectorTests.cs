using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Scoring;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdRoundScoreProjectorTests
{
    [Test]
    public async Task ExplicitExemptionRemainsVisibleAndDoesNotAwardHealthyPoints()
    {
        var fixture = new Fixture(1);
        fixture.AddCheck(fixture.Defender, AwdHealth.InternalError);
        fixture.Checks[0].Evidence.Settlement = AwdSettlementState.Exempted;
        var availability = fixture.Project().Single(x => x.TeamId == fixture.Defender).Components
            .Single(x => x.Component == AwdScoreComponent.Availability);
        await Assert.That(availability.Points).IsNull();
        await Assert.That(availability.Settlement).IsEqualTo(AwdSettlementState.Exempted);
    }

    [Test]
    public async Task LaterAdmissionRevisionDoesNotEraseExistingTeamFinalCheck()
    {
        var fixture = new Fixture(1);
        fixture.AddCheck(fixture.Defender, AwdHealth.Ok);
        var later = new AwdRoundRules { Id = Guid.NewGuid(), RoundId = fixture.Round.Id,
            CompetitionChallengeId = fixture.Rules.CompetitionChallengeId, Revision = 2,
            EffectiveAt = fixture.Start.AddMinutes(2), CheckerIntervalSeconds = 30, ServiceHealthyPoints = 100,
            Teams = fixture.Rules.Teams.Append(new() { TeamId = Guid.NewGuid() }).ToList(), Stores = fixture.Rules.Stores };
        var values = AwdRoundScoreProjector.Project([fixture.Round], [fixture.Rules, later], fixture.Issues,
            fixture.Captures, fixture.Checks, new HashSet<Guid> { fixture.Defender }, fixture.Round.ActiveEndTicks, 2, false);
        await Assert.That(values.Single().Components.Single(x => x.Component == AwdScoreComponent.Availability).Points).IsEqualTo(100L);
    }

    [Test]
    public async Task PausedActiveClockKeepsRoundOpenAfterItsOriginalWallDeadline()
    {
        var fixture = new Fixture(1);
        var values = AwdRoundScoreProjector.Project([fixture.Round], [fixture.Rules], fixture.Issues, [], [],
            new HashSet<Guid> { fixture.Defender }, TimeSpan.FromSeconds(150).Ticks, 1, false);
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Awd,
            [new(fixture.Defender, "defender", false, false)], [],
            [new(fixture.Rules.CompetitionChallengeId, "Web", "service", false)],
            AwdRounds: [new(fixture.Rules.CompetitionChallengeId, fixture.Defender, fixture.Round.Id,
                fixture.Round.StartsAt, fixture.Round.EndsAt, 1)], ProjectedAt: fixture.Start.AddHours(1),
            CompetitionStatus: CompetitionStatus.Paused, AwdAggregates: [], AwdRoundScores: values);
        var projected = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);
        await Assert.That(projected.Schema.Rounds.Single().State).IsEqualTo(ScoreboardRoundState.Running);
        await Assert.That(projected.Snapshot.Teams.Single().Slots.Single().AwdComponents
            .Single(x => x.Component == AwdScoreComponent.Availability).Points).IsNull();
    }

    [Test]
    public async Task FloorIsExplainedAfterManualAdjustmentWithoutChangingAttackComponent()
    {
        var team = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var projection = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(new(
            Guid.NewGuid(), GameMode.Awd, [new(team, "team", false, false)],
            [new(Guid.NewGuid(), team, challenge, GameplayFactKind.ManualAdjustment, DateTimeOffset.UtcNow,
                GameplayFactState.Completed, GameplayFactResult.Applied, null, Value: "-25")],
            [new(challenge, "Web", "service", false)],
            AwdAggregates: [new(team, challenge, -50, 50, 1, 0, null)], AwdMinimumTotalScore: 0));
        var row = projection.Snapshot.Teams.Single();
        await Assert.That(row.TotalScore).IsEqualTo(0);
        await Assert.That(row.AttackScore).IsEqualTo(50L);
        await Assert.That(row.GlobalAdjustments.Single(x => x.Kind == ScoreboardAdjustmentKind.ScoreFloor).NetPoints).IsEqualTo(75);
        await Assert.That(row.ScoreOutsideWindow + row.Slots.Sum(x => x.NetPoints.GetValueOrDefault())
            + row.GlobalAdjustments.Sum(x => x.NetPoints)).IsEqualTo(0);
    }

    [Test]
    public async Task MultipleCapturesUseFlagIdentityAndOnlyRoundComponentRounding()
    {
        var fixture = new Fixture(3);
        foreach (var flag in fixture.Issues)
        {
            fixture.Captures.Add(new(flag.FlagId, fixture.Attacker, fixture.Start.AddMinutes(7)));
            fixture.Captures.Add(new(flag.FlagId, fixture.Attacker, fixture.Start.AddMinutes(8)));
        }
        var result = fixture.Project();
        var attacker = result.Single(x => x.TeamId == fixture.Attacker);
        await Assert.That(attacker.AttackPoints).IsEqualTo(50);
        await Assert.That(attacker.AttackCount).IsEqualTo(3);
        await Assert.That(attacker.RoundId).IsEqualTo(fixture.Round.Id);
        await Assert.That(attacker.LastAttackAt).IsEqualTo(fixture.Start.AddMinutes(7));
        var defender = result.Single(x => x.TeamId == fixture.Defender);
        await Assert.That(defender.Components.Single(x => x.Component == AwdScoreComponent.DefenseLoss).Points).IsEqualTo(-100L);
        await Assert.That(defender.Components.Single(x => x.Component == AwdScoreComponent.Availability).Points).IsNull();
    }

    [Test]
    public async Task SharedPoolRemainsProvisionalUntilOriginalIssueWindowCloses()
    {
        var fixture = new Fixture(1);
        fixture.Rules.AttackRewardMode = AwdAttackRewardMode.SplitVictimDefensePool;
        fixture.Rules.Options = new() { DefenseMode = AwdDefenseMode.SharedPool, AttackSettlement = AwdAttackSettlement.FlagExpiry };
        fixture.Captures.Add(new(fixture.Issues[0].FlagId, fixture.Attacker, fixture.Start.AddMinutes(1)));
        var first = fixture.Project().Single(x => x.TeamId == fixture.Attacker).Components.Single(x => x.Component == AwdScoreComponent.Attack);
        await Assert.That(first.Points).IsEqualTo(100L);
        await Assert.That(first.Settlement).IsEqualTo(AwdSettlementState.Provisional);
        fixture.Captures.Add(new(fixture.Issues[0].FlagId, fixture.OtherAttacker, fixture.Start.AddMinutes(4)));
        var later = fixture.Project().Single(x => x.TeamId == fixture.Attacker).Components.Single(x => x.Component == AwdScoreComponent.Attack);
        await Assert.That(later.Points).IsEqualTo(50L);
        var final = fixture.Project(currentRound: 6).Single(x => x.TeamId == fixture.Attacker).Components.Single(x => x.Component == AwdScoreComponent.Attack);
        await Assert.That(final.Points).IsEqualTo(50L);
        await Assert.That(final.Settlement).IsEqualTo(AwdSettlementState.Final);
    }

    [Test]
    public async Task HealthyEvidenceIsRequiredAndNeverCarriesAcrossRounds()
    {
        var fixture = new Fixture(1);
        fixture.AddCheck(fixture.Defender, AwdHealth.Ok);
        await Assert.That(fixture.Project().Single(x => x.TeamId == fixture.Defender).Score).IsEqualTo(100);
        fixture.Checks.Clear();
        fixture.AddCheck(fixture.Defender, AwdHealth.InternalError);
        var unknown = fixture.Project().Single(x => x.TeamId == fixture.Defender);
        await Assert.That(unknown.Score).IsEqualTo(0);
        await Assert.That(unknown.Components.Single(x => x.Component == AwdScoreComponent.Availability).Settlement)
            .IsEqualTo(AwdSettlementState.PendingAdjudication);
        fixture.Round.Partial = true;
        fixture.Checks.Clear();
        fixture.AddCheck(fixture.Defender, AwdHealth.Ok);
        await Assert.That(fixture.Project().Single(x => x.TeamId == fixture.Defender).CompleteHealthyRound).IsFalse();
        await Assert.That(fixture.Project().Single(x => x.TeamId == fixture.Defender).Score).IsEqualTo(0);
    }

    [Test]
    public async Task WeightedStoresDoNotTurnLargeFlagCountsIntoLargeSlaBudgets()
    {
        var fixture = new Fixture(3);
        var secondStore = Guid.NewGuid();
        fixture.Rules.Stores[0].Weight = 1;
        fixture.Rules.Stores.Add(new() { StoreId = secondStore, Weight = 3, FlagsPerIssue = 1 });
        fixture.Rules.Options = new() { SlaBasis = AwdSlaBasis.ReadableFlags };
        fixture.Issues.Add(new() { FlagId = Guid.NewGuid(), RulesId = fixture.Rules.Id, StoreId = secondStore,
            TeamId = fixture.Defender, CompetitionChallengeId = fixture.Rules.CompetitionChallengeId, RoundId = fixture.Round.Id,
            State = AwdFlagIssueState.Issued, ExpiryRound = 6 });
        fixture.AddCheck(fixture.Defender, AwdHealth.Recovering);
        var details = fixture.Checks[0].Evidence;
        details.Flags[0].Read = AwdProbeOutcome.Passed;
        details.Flags[1].Read = details.Flags[2].Read = AwdProbeOutcome.TargetFailure;
        details.Flags[3].Read = AwdProbeOutcome.Passed;
        // (1/4 * 1/3 + 3/4 * 1) * 100 = 83 1/3, once per question/round.
        var sla = fixture.Project().Single(x => x.TeamId == fixture.Defender).Components
            .Single(x => x.Component == AwdScoreComponent.Availability);
        await Assert.That(sla.Points).IsEqualTo(83L);
        await Assert.That(sla.UnroundedPoints).IsEqualTo(83.333333333333m);
        details.Flags[2].Obligation = AwdFlagObligation.PlatformPending;
        await Assert.That(fixture.Project().Single(x => x.TeamId == fixture.Defender).Components
            .Single(x => x.Component == AwdScoreComponent.Availability).Points).IsNull();
    }

    [Test]
    public async Task SlaMultiplierKeepsLossAndPenaltyIndependentOfAvailability()
    {
        var fixture = new Fixture(1);
        fixture.Rules.Options = new() { Composition = AwdScoreComposition.PositiveSlaMultiplier,
            PenaltyMode = AwdPenaltyMode.Fixed, OfflinePenalty = 20 };
        fixture.AddCheck(fixture.Defender, AwdHealth.Offline);
        fixture.Captures.Add(new(fixture.Issues[0].FlagId, fixture.Attacker, fixture.Start.AddSeconds(40)));
        var defender = fixture.Project().Single(x => x.TeamId == fixture.Defender);
        await Assert.That(defender.Score).IsEqualTo(-120);
        var attacker = fixture.Project().Single(x => x.TeamId == fixture.Attacker);
        await Assert.That(attacker.Components.Single(x => x.Component == AwdScoreComponent.Attack).Points).IsNull();
        fixture.AddCheck(fixture.Attacker, AwdHealth.Ok);
        await Assert.That(fixture.Project().Single(x => x.TeamId == fixture.Attacker).AttackPoints).IsEqualTo(50);
    }

    [Test]
    public async Task MissingAverageSampleAndUnobservedInitialTimeRemainUnresolved()
    {
        var fixture = new Fixture(1);
        fixture.Rules.Options = new() { SlaSettlement = AwdSlaSettlement.SampleAverage };
        fixture.AddCheck(fixture.Defender, AwdHealth.Ok);
        await Assert.That(fixture.Project().Single(x => x.TeamId == fixture.Defender).Score).IsEqualTo(0);
        fixture.Rules.Options = new() { SlaSettlement = AwdSlaSettlement.TimeWeighted };
        await Assert.That(fixture.Project().Single(x => x.TeamId == fixture.Defender).Score).IsEqualTo(0);
    }

    private sealed class Fixture
    {
        public DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public Guid Defender { get; } = Guid.NewGuid();
        public Guid Attacker { get; } = Guid.NewGuid();
        public Guid OtherAttacker { get; } = Guid.NewGuid();
        public AwdRound Round { get; }
        public AwdRoundRules Rules { get; }
        public List<AwdFlagIssue> Issues { get; } = [];
        public List<AwdScoringCapture> Captures { get; } = [];
        public List<AwdScoringCheck> Checks { get; } = [];
        public Fixture(int flags)
        {
            Round = new() { Id = Guid.NewGuid(), Number = 1, StartsAt = Start, EndsAt = Start.AddMinutes(3), ActiveEndTicks = TimeSpan.FromMinutes(3).Ticks };
            Rules = new() { Id = Guid.NewGuid(), RoundId = Round.Id, CompetitionChallengeId = Guid.NewGuid(),
                AttackPoints = 50, VictimDefensePoolPoints = 100, ServiceHealthyPoints = 100, CheckerIntervalSeconds = 30,
                EffectiveAt = Start, EligibleTeamCount = 3,
                Stores = [new() { StoreId = Guid.NewGuid(), Weight = 1, FlagsPerIssue = flags }],
                Teams = [new() { TeamId = Defender }, new() { TeamId = Attacker }, new() { TeamId = OtherAttacker }] };
            for (var i = 0; i < flags; i++) Issues.Add(new() { FlagId = Guid.NewGuid(), RulesId = Rules.Id,
                RoundId = Round.Id, RoundNumber = 1, StoreId = Rules.Stores[0].StoreId, TeamId = Defender,
                CompetitionChallengeId = Rules.CompetitionChallengeId, ExpiryRound = 6, HistoricalExpiryRound = 6,
                State = AwdFlagIssueState.Issued });
        }

        public void AddCheck(Guid team, AwdHealth health) => Checks.Add(new(team, Rules.CompetitionChallengeId, Round.Id,
            new() { RulesId = Rules.Id, Health = health, ManifestComplete = true, FinalScheduledCheck = true,
                SampleActiveTicks = TimeSpan.FromSeconds(150).Ticks,
                Settlement = health == AwdHealth.InternalError ? AwdSettlementState.PendingAdjudication : AwdSettlementState.Final,
                Flags = Issues.Select(flag => new AwdFactFlagEvidence { FlagId = flag.FlagId,
                    Obligation = AwdFlagObligation.PlayerRequired, Read = AwdProbeOutcome.Passed }).ToList() },
            TimeSpan.FromSeconds(160).Ticks));

        public IReadOnlyList<AwdRoundScoreProjection> Project(int currentRound = 3) => AwdRoundScoreProjector.Project(
            [Round], [Rules], Issues, Captures, Checks, new HashSet<Guid> { Defender, Attacker, OtherAttacker },
            TimeSpan.FromMinutes(6).Ticks, currentRound, false);
    }
}
