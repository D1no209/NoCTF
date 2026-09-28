using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;
using NSubstitute;

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
    public async Task Invalid_persisted_configuration_is_rejected()
    {
        var invalid = CompetitionTrackConfiguration.ToPersisted(new(
            [Track("one"), Track("two")]));

        await Assert.That(() => CompetitionTrackConfiguration.FromPersisted(GameMode.Awd, invalid))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Null_track_collection_is_rejected()
    {
        await Assert.That(() => CompetitionTrackConfiguration.FromPersisted(GameMode.Ctf, null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Sso_gate_round_trips_and_is_ignored_when_tracks_are_disabled()
    {
        var providerId = Guid.CreateVersion7();
        var configuration = new CompetitionTrackConfiguration(
        [
            Track("default", isDefault: true) with
            {
                RequiredSsoProviderId = providerId
            }
        ]);

        var restored = CompetitionTrackConfiguration.FromPersisted(
            GameMode.Ctf,
            CompetitionTrackConfiguration.ToPersisted(configuration));
        var disabled = CompetitionTrackConfiguration.EffectiveFor(
            GameMode.Ctf,
            tracksEnabled: false,
            CompetitionTrackConfiguration.ToPersisted(configuration));

        await Assert.That(restored.DefaultTrack.RequiredSsoProviderId).IsEqualTo(providerId);
        await Assert.That(disabled.DefaultTrack.RequiredSsoProviderId).IsNull();
        await Assert.That(CompetitionTrackPolicy.Validate(GameMode.Ctf,
            [Track("default", isDefault: true) with
            {
                RequiredSsoProviderId = Guid.Empty
            }])).IsNotEmpty();
    }

    [Test]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    [Arguments(GameMode.Koh)]
    public async Task Disabled_tracks_use_one_public_comprehensive_default_for_every_mode(GameMode mode)
    {
        var saved = new CompetitionTrackConfiguration(
        [
            Track("formal", isDefault: true) with
            {
                IsPublicSelectable = false,
                IsInternal = true,
                EarnsScore = false,
                EarnsBlood = false,
                AffectsDynamicChallengeScore = false,
                VisibleOnLeaderboard = false,
                AffectsCompetitiveResults = false,
                InvitationCode = "secret-value"
            },
            Track("student")
        ]);

        var effective = CompetitionTrackConfiguration.EffectiveFor(
            mode,
            tracksEnabled: false,
            CompetitionTrackConfiguration.ToPersisted(saved));

        await Assert.That(effective.Tracks).HasSingleItem();
        await Assert.That(effective.DefaultTrack.Key).IsEqualTo("formal");
        await Assert.That(effective.DefaultTrack.IsInternal).IsFalse();
        await Assert.That(effective.DefaultTrack.IsPublicSelectable).IsTrue();
        await Assert.That(effective.DefaultTrack.EarnsScore).IsTrue();
        await Assert.That(effective.DefaultTrack.VisibleOnLeaderboard).IsTrue();
        await Assert.That(effective.DefaultTrack.AffectsCompetitiveResults).IsTrue();
        await Assert.That(effective.DefaultTrack.EarnsBlood).IsEqualTo(mode == GameMode.Ctf);
        await Assert.That(effective.DefaultTrack.AffectsDynamicChallengeScore)
            .IsEqualTo(mode == GameMode.Ctf);
        await Assert.That(effective.DefaultTrack.InvitationCode).IsNull();
    }

    [Test]
    [Arguments(CompetitionStatus.Running, true)]
    [Arguments(CompetitionStatus.Paused, true)]
    [Arguments(CompetitionStatus.Finished, false)]
    [Arguments(CompetitionStatus.Draft, true)]
    [Arguments(CompetitionStatus.Visible, true)]
    [Arguments(CompetitionStatus.Published, true)]
    public async Task Update_capability_only_rejects_finished_competitions(
        CompetitionStatus status,
        bool expected) =>
        await Assert.That(CompetitionTrackPolicy.CanUpdate(status)).IsEqualTo(expected);

    [Test]
    public async Task Blank_invitation_code_preserves_the_existing_code()
    {
        var competitionId = Guid.CreateVersion7();
        var track = Track("default", isDefault: true);
        var store = Substitute.For<ICompetitionTrackStore>();
        UpdateCompetitionTracksCommand? receivedCommand = null;
        store.UpdateAsync(
                Arg.Any<UpdateCompetitionTracksCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                receivedCommand = call.ArgAt<UpdateCompetitionTracksCommand>(0);
                return UpdateCompetitionTracksResult.Success(new(
                    competitionId,
                    GameMode.Ctf,
                    CompetitionStatus.Running,
                    true,
                    true,
                    [new(
                        track.Key,
                        track.Name,
                        track.IsDefault,
                        track.IsPublicSelectable,
                        track.IsInternal,
                        track.EarnsScore,
                        track.EarnsBlood,
                        track.AffectsDynamicChallengeScore,
                        track.VisibleOnLeaderboard,
                        track.AffectsCompetitiveResults,
                        RequiresInvitationCode: true)]));
            });
        var update = new UpdateCompetitionTracks(store);

        var result = await update.ExecuteAsync(new(
            competitionId,
            true,
            [track],
            [],
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow,
            [new(track.Key, "  ", ClearInvitationCode: false)]), GameMode.Ctf);

        await Assert.That(result.Succeeded).IsTrue();
        await store.Received(1).UpdateAsync(
            Arg.Any<UpdateCompetitionTracksCommand>(),
            Arg.Any<CancellationToken>());
        await Assert.That(receivedCommand).IsNotNull();
        await Assert.That(receivedCommand!.InvitationCodeUpdates).IsNotNull();
        await Assert.That(receivedCommand.InvitationCodeUpdates![0].InvitationCode).IsNull();
    }

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
                CompetitionModeConfigurationDefaults.Create(GameMode.Ctf, competitionId),
                [],
                ApprovedTeamCount: 1,
                MaxConcurrentRuntimeInstancesPerTeam: 0,
                Tracks: CompetitionTrackConfiguration.ToPersisted(new(
                    [Track("formal", isDefault: true), Track("other", isDefault: true)])),
                ApprovedTeamTrackKeys: ["missing"],
                TracksEnabled: true)),
            new GameModeCompetitionConfigurationValidator(),
            configurations);

        var errors = await gate.ValidateAsync(competitionId);

        var validationErrors = errors!;
        await Assert.That(validationErrors.Any(error =>
            error.Code == StartGateFailureCode.TrackConfigurationInvalid)).IsTrue();
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
