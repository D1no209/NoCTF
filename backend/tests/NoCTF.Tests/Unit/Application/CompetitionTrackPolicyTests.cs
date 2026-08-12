using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionTrackPolicyTests
{
    [Test]
    public async Task Valid_configuration_is_normalized_case_insensitively()
    {
        var tracks = new[]
        {
            Track(" Formal ", isDefault: true),
            Track("Student")
        };

        var errors = CompetitionTrackPolicy.Validate(GameMode.Ctf, tracks);
        var normalized = CompetitionTrackPolicy.Normalize(tracks);

        await Assert.That(errors).IsEmpty();
        await Assert.That(normalized.Tracks.Select(track => track.Key))
            .IsEquivalentTo(["formal", "student"]);
    }

    [Test]
    public async Task Duplicate_default_internal_and_non_ctf_blood_invariants_are_rejected()
    {
        var invalidInternal = Track("internal", isDefault: true) with
        {
            IsInternal = true,
            IsPublicSelectable = true
        };
        var invalidBlood = Track("student", isDefault: true) with
        {
            EarnsScore = false,
            EarnsBlood = true,
            AffectsDynamicChallengeScore = true
        };

        await Assert.That(CompetitionTrackPolicy.Validate(GameMode.Ctf,
            [invalidInternal, Track("INTERNAL")])).IsNotEmpty();
        await Assert.That(CompetitionTrackPolicy.Validate(GameMode.Awd, [invalidBlood])).IsNotEmpty();
    }

    [Test]
    public async Task Default_track_must_remain_available_to_ordinary_participants()
    {
        var hiddenDefault = Track("hidden", isDefault: true) with
        {
            IsPublicSelectable = false
        };
        var internalDefault = Track("internal", isDefault: true) with
        {
            IsPublicSelectable = false,
            IsInternal = true,
            EarnsScore = false,
            EarnsBlood = false,
            AffectsDynamicChallengeScore = false,
            VisibleOnLeaderboard = false,
            AffectsCompetitiveResults = false
        };

        await Assert.That(CompetitionTrackPolicy.Validate(GameMode.Ctf, [hiddenDefault]))
            .Contains(error => error.Contains("default track", StringComparison.OrdinalIgnoreCase));
        await Assert.That(CompetitionTrackPolicy.Validate(GameMode.Ctf, [internalDefault]))
            .Contains(error => error.Contains("default track", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task Invalid_persisted_configuration_falls_back_without_throwing()
    {
        var invalid = CompetitionTrackConfiguration.Serialize(new(
            CompetitionTrackConfiguration.CurrentSchemaVersion,
            [Track("one"), Track("two")]));

        var parsed = CompetitionTrackConfiguration.ParseOrDefault(GameMode.Awd, invalid);

        await Assert.That(parsed.DefaultTrack.Key)
            .IsEqualTo(CompetitionTrackConfiguration.DefaultTrackKey);
        await Assert.That(parsed.DefaultTrack.EarnsBlood).IsFalse();
    }

    [Test]
    public async Task Null_track_collection_is_rejected_without_throwing()
    {
        const string invalid = """{"schemaVersion":1,"tracks":null}""";

        var parsed = CompetitionTrackConfiguration.ParseOrDefault(GameMode.Ctf, invalid);

        await Assert.That(parsed.DefaultTrack.Key)
            .IsEqualTo(CompetitionTrackConfiguration.DefaultTrackKey);
        await Assert.That(CompetitionTrackConfiguration.TryParse(invalid, out _)).IsFalse();
    }

    [Test]
    [Arguments(CompetitionStatus.Running, true)]
    [Arguments(CompetitionStatus.Paused, true)]
    [Arguments(CompetitionStatus.Finished, true)]
    [Arguments(CompetitionStatus.Draft, false)]
    [Arguments(CompetitionStatus.Visible, false)]
    [Arguments(CompetitionStatus.Published, false)]
    public async Task Freeze_state_matches_first_running_boundary(
        CompetitionStatus status,
        bool expected) =>
        await Assert.That(CompetitionTrackPolicy.IsFrozen(status)).IsEqualTo(expected);

    [Test]
    public async Task Start_gate_rejects_invalid_configuration_and_missing_team_track()
    {
        var competitionId = Guid.CreateVersion7();
        var configurations = new GameModeChallengeConfigurationCatalog();
        var gate = new CompetitionStartGate(
            new StartGateStore(new(
                competitionId,
                GameMode.Ctf,
                CompetitionStatus.Published,
                GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf),
                [],
                ApprovedTeamCount: 1,
                MaxConcurrentRuntimeInstancesPerTeam: 0,
                TrackConfigurationJson: CompetitionTrackConfiguration.Serialize(new(
                    SchemaVersion: 99,
                    [Track("formal", isDefault: true)])),
                ApprovedTeamTrackKeys: ["missing"])),
            new GameModeCompetitionConfigurationValidator(),
            configurations);

        var errors = await gate.ValidateAsync(competitionId);

        var validationErrors = errors!;
        await Assert.That(validationErrors.Any(error =>
            error.Code == StartGateFailureCode.TrackConfigurationInvalid)).IsTrue();
        await Assert.That(validationErrors.Any(error =>
            error.Code == StartGateFailureCode.TeamTrackInvalid)).IsTrue();
    }

    private static CompetitionTrackDefinition Track(string key, bool isDefault = false) => new(
        key,
        key.Trim(),
        isDefault,
        true,
        false,
        true,
        true,
        true,
        true,
        true);

    private sealed class StartGateStore(CompetitionStartGateSnapshot snapshot)
        : ICompetitionStartGateStore
    {
        public Task<CompetitionStartGateSnapshot?> LoadAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStartGateSnapshot?>(
                competitionId == snapshot.CompetitionId ? snapshot : null);
    }
}
