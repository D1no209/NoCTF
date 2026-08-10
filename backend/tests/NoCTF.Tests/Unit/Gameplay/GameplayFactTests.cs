using System.Globalization;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.Gameplay;

public sealed class GameplayFactTests
{
    [Test]
    [Arguments(GameplayFactKind.FlagAttempt, GameplayFactResult.Correct)]
    [Arguments(GameplayFactKind.BreakAttempt, GameplayFactResult.Wrong)]
    [Arguments(GameplayFactKind.FixAttempt, GameplayFactResult.Rejected)]
    [Arguments(GameplayFactKind.HintUnlock, GameplayFactResult.Unlocked)]
    [Arguments(GameplayFactKind.ManualAdjustment, GameplayFactResult.Applied)]
    [Arguments(GameplayFactKind.AwdServiceTransition, GameplayFactResult.ServiceDown)]
    [Arguments(GameplayFactKind.KohControlObservation, GameplayFactResult.Controlled)]
    public async Task Kinds_and_results_are_stable(GameplayFactKind kind, GameplayFactResult result)
    {
        await Assert.That(Enum.IsDefined(kind)).IsTrue();
        await Assert.That(Enum.IsDefined(result)).IsTrue();
    }

    [Test]
    [Arguments("-10", -10)]
    [Arguments("25", 25)]
    [Arguments("+1", null)]
    [Arguments("01", null)]
    [Arguments("0", null)]
    [Arguments("2147483648", null)]
    public async Task Manual_adjustment_requires_nonzero_canonical_int32(string value, int? expected)
    {
        var parsed = int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta)
                     && delta != 0
                     && delta.ToString(CultureInfo.InvariantCulture) == value
            ? delta
            : (int?)null;

        await Assert.That(parsed).IsEqualTo(expected);
    }

    [Test]
    public async Task Ctf_projector_consumes_completed_gameplay_fact_and_manual_adjustment()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-08-10T00:00:00Z");
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Ctf,
            [new(teamId, "team", false, false, at.AddMinutes(-1))],
            [
                new(Guid.NewGuid(), teamId, challengeId, GameplayFactKind.FlagAttempt, at,
                    GameplayFactState.Completed, GameplayFactResult.Correct, null),
                new(Guid.NewGuid(), teamId, challengeId, GameplayFactKind.ManualAdjustment, at.AddSeconds(1),
                    GameplayFactState.Completed, GameplayFactResult.Applied, null, Value: "25")
            ],
            [new(challengeId, "web", "challenge", false)]);

        var entry = new CtfLeaderboardProjector().Project(input).Entries.Single();

        await Assert.That(entry.TeamId).IsEqualTo(teamId);
        await Assert.That(entry.SolveCount).IsEqualTo(1);
        await Assert.That(entry.Score).IsGreaterThan(25);
    }

    [Test]
    public async Task Koh_projector_counts_repeated_control_observations()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-08-10T00:00:00Z");
        var facts = new[]
        {
            new LeaderboardGameplayFact(Guid.NewGuid(), teamId, challengeId, GameplayFactKind.KohControlObservation,
                at, GameplayFactState.Completed, GameplayFactResult.Controlled, null),
            new LeaderboardGameplayFact(Guid.NewGuid(), teamId, challengeId, GameplayFactKind.KohControlObservation,
                at.AddSeconds(1), GameplayFactState.Completed, GameplayFactResult.Controlled, null)
        };
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Koh,
            [new(teamId, "team", false, false)], facts,
            [new(challengeId, "pwn", "king", false)]);

        var entry = new KohLeaderboardProjector().Project(input).Entries.Single();

        await Assert.That(entry.SolveCount).IsEqualTo(2);
    }
}
