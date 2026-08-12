using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CompetitionTrackLeaderboardTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-08-12T00:00:00Z");

    [Test]
    public async Task Ctf_tracks_rank_independently_and_internal_solves_do_not_affect_dynamic_score_or_blood()
    {
        var challengeId = Guid.NewGuid();
        var formalA = Team("formal-a", "formal", earnsBlood: true, affectsDynamic: true);
        var formalB = Team("formal-b", "formal", earnsBlood: true, affectsDynamic: true);
        var student = Team("student", "student", earnsBlood: false, affectsDynamic: false);
        var internalTeam = Team("internal", "internal", earnsScore: false, earnsBlood: false,
            affectsDynamic: false, visible: false, competitive: false);
        var facts = new[]
        {
            Solve(internalTeam.Id, challengeId, Start),
            Solve(student.Id, challengeId, Start.AddSeconds(1)),
            Solve(formalA.Id, challengeId, Start.AddSeconds(2)),
            Solve(formalB.Id, challengeId, Start.AddSeconds(3))
        };
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Ctf,
            [formalA, formalB, student, internalTeam],
            facts,
            [new(challengeId, "web", "track-test", false)]);

        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);
        var formalEntries = result.Entries.Where(entry => entry.TrackKey == "formal").ToArray();
        var studentEntry = result.Entries.Single(entry => entry.TeamId == student.Id);

        await Assert.That(formalEntries.Select(entry => entry.Rank)).IsEquivalentTo([1, 2]);
        await Assert.That(studentEntry.Rank).IsEqualTo(1);
        await Assert.That(result.Entries.Any(entry => entry.TeamId == internalTeam.Id)).IsFalse();
        await Assert.That(result.Challenges.Single().CurrentScore).IsNotNull();
        await Assert.That(result.Entries.Single(entry => entry.TeamId == formalA.Id)
            .Cells.Single().BloodRank).IsEqualTo(LeaderboardBloodRank.First);
        await Assert.That(studentEntry.Cells.Single().BloodRank).IsNull();
    }

    [Test]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    [Arguments(GameMode.Koh)]
    public async Task Non_ctf_tracks_rank_independently_and_internal_team_remains_zero(GameMode mode)
    {
        var challengeId = Guid.NewGuid();
        var publicA = Team("public-a", "formal", earnsBlood: false, affectsDynamic: false);
        var publicB = Team("public-b", "student", earnsBlood: false, affectsDynamic: false);
        var internalTeam = Team("internal", "internal", earnsScore: false, earnsBlood: false,
            affectsDynamic: false, visible: false, competitive: false);
        var facts = mode switch
        {
            GameMode.Awd => new[]
            {
                new LeaderboardGameplayFact(Guid.NewGuid(), publicA.Id, challengeId,
                    GameplayFactKind.AwdServiceTransition, Start,
                    GameplayFactState.Completed, GameplayFactResult.ServiceUp, null)
            },
            GameMode.Awdp => new[]
            {
                new LeaderboardGameplayFact(Guid.NewGuid(), publicA.Id, challengeId,
                    GameplayFactKind.FixAttempt, Start,
                    GameplayFactState.Completed, GameplayFactResult.Correct, null)
            },
            GameMode.Koh => new[]
            {
                new LeaderboardGameplayFact(Guid.NewGuid(), publicA.Id, challengeId,
                    GameplayFactKind.KohControlObservation, Start,
                    GameplayFactState.Completed, GameplayFactResult.Controlled, null)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(new(
            Guid.NewGuid(),
            mode,
            [publicA, publicB, internalTeam],
            facts,
            [new(challengeId, "pwn", "track-test", false)]));

        await Assert.That(result.Entries.Single(entry => entry.TeamId == publicA.Id).Rank).IsEqualTo(1);
        await Assert.That(result.Entries.Single(entry => entry.TeamId == publicB.Id).Rank).IsEqualTo(1);
        await Assert.That(result.Entries.Any(entry => entry.TeamId == internalTeam.Id)).IsFalse();
    }

    [Test]
    public async Task Manual_adjustment_does_not_restore_score_for_a_non_scoring_track()
    {
        var challengeId = Guid.NewGuid();
        var publicTeam = Team("public", "formal", earnsBlood: true, affectsDynamic: true);
        var internalTeam = Team("internal", "internal", earnsScore: false, earnsBlood: false,
            affectsDynamic: false, visible: false, competitive: false);
        var facts = new[]
        {
            new LeaderboardGameplayFact(Guid.NewGuid(), internalTeam.Id, challengeId,
                GameplayFactKind.ManualAdjustment, Start,
                GameplayFactState.Completed, GameplayFactResult.Applied, null,
                Value: "500"),
            Solve(publicTeam.Id, challengeId, Start.AddSeconds(1))
        };
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(new(
            Guid.NewGuid(),
            GameMode.Ctf,
            [publicTeam, internalTeam],
            facts,
            [new(challengeId, "web", "track-test", false)]));

        await Assert.That(result.Entries.Any(entry => entry.TeamId == internalTeam.Id)).IsFalse();
    }

    [Test]
    public async Task Non_scoring_track_can_affect_dynamic_score_without_getting_an_entry()
    {
        var challengeId = Guid.NewGuid();
        var formal = Team("formal", "formal", earnsBlood: true, affectsDynamic: true);
        var calibration = Team("calibration", "calibration", earnsScore: false, earnsBlood: false,
            affectsDynamic: true, visible: false, competitive: false);
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(new(
            Guid.NewGuid(),
            GameMode.Ctf,
            [formal, calibration],
            [
                Solve(calibration.Id, challengeId, Start),
                Solve(formal.Id, challengeId, Start.AddSeconds(1))
            ],
            [new(challengeId, "web", "track-test", false)]));

        await Assert.That(result.Entries.Any(entry => entry.TeamId == calibration.Id)).IsFalse();
        await Assert.That(result.Challenges.Single().CurrentScore!.Value).IsLessThan(500);
        await Assert.That(result.Entries.Single().Cells.Single().BloodRank)
            .IsEqualTo(LeaderboardBloodRank.First);
    }

    [Test]
    public async Task Blood_order_uses_occurred_at_then_gameplay_fact_id_across_eligible_tracks()
    {
        var challengeId = Guid.NewGuid();
        var firstTeam = Team("first", "formal", earnsBlood: true, affectsDynamic: true);
        var secondTeam = Team("second", "student", earnsBlood: true, affectsDynamic: true);
        var laterId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var earlierId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var facts = new[]
        {
            Solve(firstTeam.Id, challengeId, Start) with { GameplayFactId = earlierId },
            Solve(secondTeam.Id, challengeId, Start) with { GameplayFactId = laterId }
        };
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(new(
            Guid.NewGuid(), GameMode.Ctf, [firstTeam, secondTeam], facts,
            [new(challengeId, "web", "track-test", false)]));

        await Assert.That(result.Entries.Single(entry => entry.TeamId == firstTeam.Id)
            .Cells.Single().BloodRank).IsEqualTo(LeaderboardBloodRank.First);
        await Assert.That(result.Entries.Single(entry => entry.TeamId == secondTeam.Id)
            .Cells.Single().BloodRank).IsEqualTo(LeaderboardBloodRank.Second);
    }

    [Test]
    public async Task Legacy_default_team_projection_is_unchanged()
    {
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var fact = Solve(teamId, challengeId, Start);
        var legacy = new LeaderboardTeamFact(teamId, "legacy", false, false, Start.AddMinutes(-1));
        var explicitDefault = Team("legacy", CompetitionTrackConfiguration.DefaultTrackKey,
            earnsBlood: true, affectsDynamic: true) with { Id = teamId };
        var engine = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog());
        var legacyResult = engine.Project(new(
            Guid.NewGuid(), GameMode.Ctf, [legacy], [fact],
            [new(challengeId, "web", "track-test", false)]));
        var explicitResult = engine.Project(new(
            Guid.NewGuid(), GameMode.Ctf, [explicitDefault], [fact],
            [new(challengeId, "web", "track-test", false)]));

        await Assert.That(explicitResult.Entries).IsEquivalentTo(legacyResult.Entries);
        await Assert.That(explicitResult.Challenges).IsEquivalentTo(legacyResult.Challenges);
    }

    [Test]
    public async Task Internal_koh_control_preserves_the_last_public_king()
    {
        var challengeId = Guid.NewGuid();
        var publicTeam = Team("public", "formal", earnsBlood: false, affectsDynamic: false);
        var internalTeam = Team("internal", "internal", earnsScore: false, earnsBlood: false,
            affectsDynamic: false, visible: false, competitive: false);
        var observations = new[]
        {
            new LeaderboardGameplayFact(Guid.NewGuid(), publicTeam.Id, challengeId,
                GameplayFactKind.KohControlObservation, Start, GameplayFactState.Completed,
                GameplayFactResult.Controlled, null),
            new LeaderboardGameplayFact(Guid.NewGuid(), internalTeam.Id, challengeId,
                GameplayFactKind.KohControlObservation, Start.AddSeconds(10), GameplayFactState.Completed,
                GameplayFactResult.Controlled, null)
        };
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(new(
            Guid.NewGuid(), GameMode.Koh, [publicTeam, internalTeam], observations,
            [new(challengeId, "pwn", "track-test", false)]));

        var publicEntry = result.Entries.Single();
        await Assert.That(publicEntry.TeamId).IsEqualTo(publicTeam.Id);
        await Assert.That(publicEntry.SolveCount).IsEqualTo(2);
        await Assert.That(publicEntry.Score).IsGreaterThan(0);
    }

    [Test]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    [Arguments(GameMode.Koh)]
    public async Task Internal_competitive_facts_do_not_change_public_scores(GameMode mode)
    {
        var challengeId = Guid.NewGuid();
        var publicTeam = Team("public", "formal", earnsBlood: false, affectsDynamic: false);
        var internalTeam = Team("internal", "internal", earnsScore: false, earnsBlood: false,
            affectsDynamic: false, visible: false, competitive: false);
        var fact = mode switch
        {
            GameMode.Awd => new LeaderboardGameplayFact(Guid.NewGuid(), internalTeam.Id, challengeId,
                GameplayFactKind.FlagAttempt, Start, GameplayFactState.Completed,
                GameplayFactResult.Correct, null, GameplayFactReferenceKind.AwdRound,
                Guid.NewGuid(), publicTeam.Id),
            GameMode.Awdp => new LeaderboardGameplayFact(Guid.NewGuid(), internalTeam.Id, challengeId,
                GameplayFactKind.BreakAttempt, Start, GameplayFactState.Completed,
                GameplayFactResult.Correct, null, VictimTeamId: publicTeam.Id),
            GameMode.Koh => new LeaderboardGameplayFact(Guid.NewGuid(), internalTeam.Id, challengeId,
                GameplayFactKind.KohControlObservation, Start, GameplayFactState.Completed,
                GameplayFactResult.Controlled, null),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(new(
            Guid.NewGuid(), mode, [publicTeam, internalTeam], [fact],
            [new(challengeId, "pwn", "track-test", false)]));

        await Assert.That(result.Entries.Single().TeamId).IsEqualTo(publicTeam.Id);
        await Assert.That(result.Entries.Single().Score).IsEqualTo(0);
        await Assert.That(result.Entries.Single().Cells).IsEmpty();
    }

    private static LeaderboardTeamFact Team(
        string name,
        string trackKey,
        bool earnsScore = true,
        bool earnsBlood = true,
        bool affectsDynamic = true,
        bool visible = true,
        bool competitive = true) => new(
        Guid.NewGuid(),
        name,
        false,
        false,
        Start.AddMinutes(-1),
        trackKey,
        earnsScore,
        earnsBlood,
        affectsDynamic,
        visible,
        competitive);

    private static LeaderboardGameplayFact Solve(
        Guid teamId,
        Guid challengeId,
        DateTimeOffset occurredAt) => new(
        Guid.NewGuid(),
        teamId,
        challengeId,
        GameplayFactKind.FlagAttempt,
        occurredAt,
        GameplayFactState.Completed,
        GameplayFactResult.Correct,
        null);
}
