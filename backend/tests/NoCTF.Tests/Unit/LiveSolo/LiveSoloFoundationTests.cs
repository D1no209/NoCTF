using NoCTF.Application.Competitions.Modes;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.LiveSolo.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.LiveSolo;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloFoundationTests
{
    [Test]
    public async Task Defaults_are_disabled_and_use_the_confirmed_round_media_and_capacity_values()
    {
        var value = (LiveSoloCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.LiveSolo, Guid.NewGuid());
        await Assert.That(value.Enabled || value.RecordingEnabled || value.ParticipantsMayViewOpponents).IsFalse();
        await Assert.That(value.RequiredWins).IsEqualTo(2);
        await Assert.That(value.CountdownSeconds).IsEqualTo(5);
        await Assert.That(value.QuestionIntervalSeconds).IsEqualTo(180);
        await Assert.That(value.RoundLimitSeconds).IsEqualTo(900);
        await Assert.That(value.PublicDelaySeconds).IsEqualTo(60);
        await Assert.That(value.RecordingRetentionDays).IsEqualTo(30);
        await Assert.That(value.MaximumConcurrentMatches).IsEqualTo(4);
        await Assert.That(value.MaximumRosterMembers).IsEqualTo(2);
        await Assert.That(value.MaximumViewers).IsEqualTo(50);
        await Assert.That(LiveSoloConfigurationValidator.Validate(value)).IsEmpty();
        await Assert.That(new GameModeChallengeConfigurationCatalog().CreateDefaultDefinition(GameMode.LiveSolo, Guid.NewGuid()))
            .IsTypeOf<LiveSoloChallengeDefinition>();
    }

    [Test]
    public async Task Overlapping_match_and_competition_pauses_are_deducted_once_and_clamped_to_the_round()
    {
        var start = DateTimeOffset.Parse("2026-10-08T12:00:00Z");
        var pauses = new[] {
            new LiveSoloPauseInterval { Source = LiveSoloPauseSource.Match, StartedAt = start.AddSeconds(10), EndedAt = start.AddSeconds(30) },
            new LiveSoloPauseInterval { Source = LiveSoloPauseSource.Competition, StartedAt = start.AddSeconds(20), EndedAt = start.AddSeconds(40) },
            new LiveSoloPauseInterval { Source = LiveSoloPauseSource.Match, StartedAt = start.AddSeconds(-10), EndedAt = start.AddSeconds(5) },
            new LiveSoloPauseInterval { Source = LiveSoloPauseSource.Match, StartedAt = start.AddSeconds(50) } };
        await Assert.That(LiveSoloActiveClock.Elapsed(start, start.AddSeconds(60), pauses)).IsEqualTo(TimeSpan.FromSeconds(15));
        await Assert.That(LiveSoloActiveClock.Paused(pauses)).IsTrue();
        await Assert.That(LiveSoloActiveClock.Elapsed(start, start.AddSeconds(-1), pauses)).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Typed_configuration_roundtrips_and_rejects_mixed_mode_branches()
    {
        var competition = Guid.NewGuid();
        var value = (LiveSoloCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.LiveSolo, competition);
        value.StageRules.Add(new() { CompetitionId = competition, Lane = LiveSoloBracketLane.GrandFinal, Stage = 1, RequiredWins = 3 });
        var contract = CompetitionModeConfigurationContractMapper.FromDomain(value);
        await Assert.That(CompetitionModeConfigurationContractMapper.HasValidShape(contract)).IsTrue();
        var roundtrip = (LiveSoloCompetitionModeConfiguration)CompetitionModeConfigurationContractMapper.ToDomain(competition, GameMode.LiveSolo, contract);
        await Assert.That(roundtrip.StageRules.Single().RequiredWins).IsEqualTo(3);
        await Assert.That(LiveSoloConfigurationMapping.ToContract(roundtrip).StageRules).IsEquivalentTo(contract.LiveSolo!.StageRules);
        await Assert.That(roundtrip.PublicDelaySeconds).IsEqualTo(contract.LiveSolo.PublicDelaySeconds);
        contract.Koh = new(5, 10);
        await Assert.That(CompetitionModeConfigurationContractMapper.HasValidShape(contract)).IsFalse();
    }

    [Test]
    public async Task Release_offsets_must_start_at_zero_and_stay_before_the_round_limit()
    {
        var group = new LiveSoloQuestionGroup { Name = "Group", Items = [
            new() { Position = 0, CompetitionChallengeId = Guid.NewGuid() },
            new() { Position = 1, CompetitionChallengeId = Guid.NewGuid() } ] };
        await Assert.That(LiveSoloConfigurationValidator.ValidateGroup(group, 900, 180)).IsEmpty();
        group.Items[1].OpenOffsetSeconds = 900;
        await Assert.That(LiveSoloConfigurationValidator.ValidateGroup(group, 900, 180)).IsNotEmpty();
        group.Items[1].OpenOffsetSeconds = 0;
        await Assert.That(LiveSoloConfigurationValidator.ValidateGroup(group, 900, 180)).IsNotEmpty();
    }

    [Test]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    [Arguments(GameMode.Koh)]
    public async Task Existing_modes_keep_ordinary_resource_and_scoring_capabilities(GameMode mode)
    {
        var capability = CompetitionModeCapabilities.For(mode);
        await Assert.That(capability.OrdinaryPlayerChallengeAccess && capability.OrdinaryScoreboard && capability.WriteUpBenefitDiscount).IsTrue();
        await Assert.That(capability.ScopedExecution).IsFalse();
        var scoped = CompetitionModeCapabilities.For(GameMode.LiveSolo);
        await Assert.That(scoped.OrdinaryPlayerChallengeAccess || scoped.OrdinaryScoreboard || scoped.BloodAwards || scoped.PaidHints || scoped.WriteUpBenefitDiscount).IsFalse();
        await Assert.That(scoped.ScopedExecution).IsTrue();
    }

    [Test]
    public async Task Opaque_runtime_scopes_do_not_change_ordinary_active_slot_keys()
    {
        var challenge = Guid.NewGuid(); var team = Guid.NewGuid();
        var runtime = new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionChallengeId = challenge, TeamId = team };
        var original = $"competition-challenge:{challenge:N}:team:{team:N}:purpose:0";
        await Assert.That(ActiveRuntimeSlot.CreateKey(runtime)).IsEqualTo(original);
        runtime.ExecutionScopeId = Guid.NewGuid(); var first = ActiveRuntimeSlot.CreateKey(runtime);
        await Assert.That(first).StartsWith(original + ":scope:");
        runtime.ExecutionScopeId = Guid.NewGuid();
        await Assert.That(ActiveRuntimeSlot.CreateKey(runtime)).IsNotEqualTo(first);
        await Assert.That(ActiveRuntimeSlot.CreateKey(runtime).Length <= 160).IsTrue();
    }
}
