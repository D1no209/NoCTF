using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CtfPatchVerificationExperimentTests
{
    private static readonly DateTimeOffset StartedAt =
        DateTimeOffset.Parse("2026-09-15T00:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    public async Task V2_definition_upgrades_to_flag_submission()
    {
        var configuration = CtfConfigurationUpgrader.ParseChallenge(
            """{"schemaVersion":2}""");

        await Assert.That(configuration.SchemaVersion).IsEqualTo(3);
        await Assert.That(configuration.InteractionKind)
            .IsEqualTo(CtfInteractionKind.FlagSubmission);
    }

    [Test]
    public async Task Ctf_rules_stay_v2_while_definitions_use_v3()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        using var rules = JsonDocument.Parse(catalog.GetDefaultJson(GameMode.Ctf));
        using var definition = JsonDocument.Parse(
            catalog.GetDefaultDefinitionJson(GameMode.Ctf));

        await Assert.That(rules.RootElement.GetProperty("schemaVersion").GetInt32())
            .IsEqualTo(2);
        await Assert.That(definition.RootElement.GetProperty("schemaVersion").GetInt32())
            .IsEqualTo(3);
    }

    [Test]
    public async Task Flag_submission_definition_rejects_patch_fields()
    {
        var errors = new GameModeChallengeConfigurationCatalog().ValidateDefinition(
            GameMode.Ctf,
            """
            {
              "schemaVersion": 3,
              "interactionKind": 0,
              "patchEntrypoint": "fix.sh"
            }
            """);

        await Assert.That(errors)
            .Contains("FlagSubmission definitions cannot contain PatchVerification settings.");
    }

    [Test]
    public async Task Patch_fix_scores_as_one_ctf_solve_and_wrong_attempt_uses_ctf_penalty()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var rules = JsonSerializer.Serialize(new CtfChallengeConfiguration(
            CtfConfiguration.CurrentSchemaVersion,
            null,
            null,
            WrongSubmissionPenalty: 7,
            MaxPatchAttempts: 10), JsonOptions);
        var challenge = new LeaderboardChallengeFact(
            challengeId,
            "Pwn",
            "Repair the service",
            false,
            rules,
            InteractionKind: CtfInteractionKind.PatchVerification);
        var facts = new[]
        {
            Fact(teamId, challengeId, 1, GameplayFactResult.Wrong),
            Fact(teamId, challengeId, 2, GameplayFactResult.Correct),
            Fact(teamId, challengeId, 3, GameplayFactResult.Correct)
        };
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Ctf,
            [new LeaderboardTeamFact(teamId, "Patchers", false, false)],
            facts,
            [challenge],
            ProjectedAt: StartedAt.AddMinutes(1),
            CompetitionStatus: CompetitionStatus.Running);
        var engine = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog());

        var output = engine.ProjectOutputs(input);
        var currentScore = output.Legacy.Challenges.Single().CurrentScore;
        var team = output.Scoreboard.Snapshot.Teams.Single();

        await Assert.That(output.Legacy.Entries.Single().SolveCount).IsEqualTo(1);
        await Assert.That(output.Legacy.Entries.Single().Score).IsEqualTo(currentScore - 7);
        await Assert.That(team.Achievements).HasSingleItem();
        await Assert.That(team.Achievements![0].Kind).IsEqualTo(ScoreboardEntryKind.Solve);
        await Assert.That(team.Slots.Single().Entries.Count(entry =>
            entry.Kind == ScoreboardEntryKind.Solve
            && entry.Outcome == ScoreboardEntryOutcome.Succeeded)).IsEqualTo(2);
    }

    [Test]
    public async Task Platform_failed_patch_fact_does_not_reduce_ctf_score()
    {
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challenge = new LeaderboardChallengeFact(
            challengeId,
            "Web",
            "Patch",
            false,
            JsonSerializer.Serialize(new CtfChallengeConfiguration(
                CtfConfiguration.CurrentSchemaVersion,
                null,
                null,
                WrongSubmissionPenalty: 50), JsonOptions),
            InteractionKind: CtfInteractionKind.PatchVerification);
        var platformFailure = Fact(teamId, challengeId, 1, null) with
        {
            State = GameplayFactState.PlatformFailed,
            FailureCode = GameplayFactFailureCode.PatchVerificationPlatformFailed
        };
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Ctf,
            [new LeaderboardTeamFact(teamId, "Stable", false, false)],
            [platformFailure],
            [challenge],
            ProjectedAt: StartedAt.AddMinutes(1),
            CompetitionStatus: CompetitionStatus.Running);

        var output = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog())
            .ProjectOutputs(input);

        await Assert.That(output.Legacy.Entries.Single().Score).IsEqualTo(0);
    }

    private static LeaderboardGameplayFact Fact(
        Guid teamId,
        Guid challengeId,
        int seconds,
        GameplayFactResult? result) => new(
            Guid.CreateVersion7(StartedAt.AddSeconds(seconds)),
            teamId,
            challengeId,
            GameplayFactKind.FixAttempt,
            StartedAt.AddSeconds(seconds),
            GameplayFactState.Completed,
            result,
            null);
}
