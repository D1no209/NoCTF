using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public sealed class GameplayFactPracticeAdmissionTests
{
    [Test]
    [Arguments(false, GameMode.Ctf, CompetitionStatus.Finished, GameplayFactAdmissionFailureCode.CompetitionFinished)]
    [Arguments(true, GameMode.Awd, CompetitionStatus.Finished, GameplayFactAdmissionFailureCode.CompetitionFinished)]
    [Arguments(true, GameMode.Ctf, CompetitionStatus.Paused, GameplayFactAdmissionFailureCode.CompetitionPaused)]
    public async Task Check_limits_practice_to_enabled_finished_ctf(
        bool practiceEnabled,
        GameMode mode,
        CompetitionStatus status,
        GameplayFactAdmissionFailureCode expected)
    {
        var now = DateTimeOffset.Parse("2026-09-11T08:00:00Z");
        var result = GameplayFactAdmissionPolicy.Check(
            Snapshot(now, mode, status, practiceEnabled),
            GameplayFactKind.FlagAttempt,
            new(true, false, 1, null),
            now);

        await Assert.That(result.FailureCode).IsEqualTo(expected);
    }

    [Test]
    public async Task Check_accepts_the_exact_practice_boundary_without_attempt_limits()
    {
        var end = DateTimeOffset.Parse("2026-09-11T08:00:00Z");
        var snapshot = Snapshot(
            end,
            GameMode.Ctf,
            CompetitionStatus.Finished,
            practiceEnabled: true) with
        {
            AcceptedFlagAttempts = 100,
            PracticeRuntimeState = PracticeRuntimeAdmissionState.Running
        };

        var result = GameplayFactAdmissionPolicy.Check(
            snapshot,
            GameplayFactKind.FlagAttempt,
            new(true, false, 1, null),
            end);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Check_requires_a_running_runtime_when_practice_has_one()
    {
        var now = DateTimeOffset.Parse("2026-09-11T08:00:00Z");
        var snapshot = Snapshot(
            now,
            GameMode.Ctf,
            CompetitionStatus.Finished,
            practiceEnabled: true) with
        {
            PracticeRuntimeState = PracticeRuntimeAdmissionState.NotRunning
        };

        var result = GameplayFactAdmissionPolicy.Check(
            snapshot,
            GameplayFactKind.FlagAttempt,
            new(true, false, null, null),
            now);

        await Assert.That(result.FailureCode)
            .IsEqualTo(GameplayFactAdmissionFailureCode.RuntimeNotRunning);
    }

    private static GameplayFactAdmissionSnapshot Snapshot(
        DateTimeOffset officialEnd,
        GameMode mode,
        CompetitionStatus status,
        bool practiceEnabled) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        mode,
        CompetitionModeConfigurationDefaults.Create(mode, Guid.NewGuid()),
        new NoCTF.GameModes.Registration.GameModeChallengeConfigurationCatalog()
            .CreateDefaultRules(mode, Guid.NewGuid()),
        0,
        0,
        status,
        officialEnd.AddHours(-2),
        officialEnd,
        false,
        false,
        true,
        false,
        false,
        true,
        true,
        OfficialEndAt: officialEnd,
        PracticeModeEnabled: practiceEnabled);
}
