using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Tests.Unit.Application;

public class TeamRegistrationTests
{
    [Test]
    [Arguments(GameMode.Ctf, true, true)]
    [Arguments(GameMode.Ctf, false, false)]
    [Arguments(GameMode.Awdp, true, false)]
    public async Task Finished_competition_allows_only_enabled_CTF_practice_registration(GameMode mode, bool enabled, bool allowed)
    {
        var store = new Store(new(CompetitionStatus.Finished, false, false, Mode: mode, PracticeModeEnabled: enabled));
        var result = await new CreateTeam(store).ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), "practice", DateTimeOffset.UtcNow, "default"));
        await Assert.That(result.Succeeded).IsEqualTo(allowed);
        if (allowed) await Assert.That(store.Status).IsEqualTo(TeamRegistrationStatus.Approved);
        else await Assert.That(result.FailureCode).IsEqualTo(TeamRegistrationFailure.RegistrationClosed);
    }

    [Test]
    public async Task CreateTeam_Creates_an_unregistered_editable_team()
    {
        var store = new Store(new(CompetitionStatus.Published, true, false));
        var command = new CreateTeamCommand(Guid.NewGuid(), Guid.NewGuid(), "alpha", DateTimeOffset.UtcNow, "default");

        var result = await new CreateTeam(store).ExecuteAsync(command);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Status).IsEqualTo(TeamRegistrationStatus.Unregistered);
        await Assert.That(result.Value!.CaptainId).IsEqualTo(command.UserId);
    }

    [Test]
    public async Task CreateTeam_RunningCompetitionRejectsRegistration()
    {
        var store = new Store(new(CompetitionStatus.Running, true, false));
        var result = await new CreateTeam(store).ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), "alpha", DateTimeOffset.UtcNow, "default"));
        await Assert.That(result.FailureCode).IsEqualTo(TeamRegistrationFailure.RegistrationClosed);
    }

    [Test]
    public async Task CreateTeam_RunningCompetitionAllowsRegistrationWhenConfigured()
    {
        var store = new Store(new(
            CompetitionStatus.Running,
            AutoApprove: false,
            CompetitionDeleted: false,
            AllowWhileRunning: true));

        var result = await new CreateTeam(store).ExecuteAsync(new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "alpha",
            DateTimeOffset.UtcNow,
            "default"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Status).IsEqualTo(TeamRegistrationStatus.Unregistered);
    }

    [Test]
    public async Task CreateTeam_Requires_an_explicit_publicly_selectable_track()
    {
        var configuration = new CompetitionTrackConfiguration(
        [
            Track("formal", isDefault: true, publicSelectable: true),
            Track("invite", publicSelectable: false),
            Track("internal", publicSelectable: false, isInternal: true)
        ]);
        var policy = new TeamRegistrationPolicy(
            CompetitionStatus.Published,
            AutoApprove: true,
            CompetitionDeleted: false,
            Mode: GameMode.Ctf,
            Tracks: CompetitionTrackConfiguration.ToPersisted(configuration),
            TracksEnabled: true);
        var store = new Store(policy);
        var create = new CreateTeam(store);

        var defaultResult = await create.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "defaulted", DateTimeOffset.UtcNow, ""));
        var publicResult = await create.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "public", DateTimeOffset.UtcNow, " FORMAL "));
        var hiddenResult = await create.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "hidden", DateTimeOffset.UtcNow, "invite"));
        var internalResult = await create.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "internal", DateTimeOffset.UtcNow, "internal"));

        await Assert.That(defaultResult.FailureCode).IsEqualTo(TeamRegistrationFailure.TrackNotFound);
        await Assert.That(publicResult.Value!.TrackKey).IsEqualTo("formal");
        await Assert.That(hiddenResult.FailureCode)
            .IsEqualTo(TeamRegistrationFailure.TrackNotPublicSelectable);
        await Assert.That(internalResult.FailureCode)
            .IsEqualTo(TeamRegistrationFailure.TrackNotPublicSelectable);
    }

    [Test]
    public async Task CreateTeam_Defers_protected_track_requirements_until_submission()
    {
        var configuration = new CompetitionTrackConfiguration(
        [
            Track("formal", isDefault: true),
            Track("invite", invitationCode: "let-me-in")
        ]);
        var policy = new TeamRegistrationPolicy(
            CompetitionStatus.Published,
            AutoApprove: true,
            CompetitionDeleted: false,
            Mode: GameMode.Ctf,
            Tracks: CompetitionTrackConfiguration.ToPersisted(configuration),
            TracksEnabled: true);
        var store = new Store(policy);
        var create = new CreateTeam(store);

        var missing = await create.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "missing", DateTimeOffset.UtcNow, "invite"));
        var invalid = await create.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "invalid", DateTimeOffset.UtcNow, "invite", "wrong-code"));
        var valid = await create.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "valid", DateTimeOffset.UtcNow, "invite", " let-me-in "));

        await Assert.That(missing.Succeeded).IsTrue();
        await Assert.That(invalid.Succeeded).IsTrue();
        await Assert.That(valid.Succeeded).IsTrue();
        await Assert.That(valid.Value!.TrackKey).IsEqualTo("invite");
    }

    [Test]
    public async Task CreateTeam_Disabled_tracks_allow_omitted_track_and_ignore_invitation_data()
    {
        var configuration = new CompetitionTrackConfiguration(
        [
            Track("formal", isDefault: true),
            Track("invite", invitationCode: "let-me-in")
        ]);
        var store = new Store(new TeamRegistrationPolicy(
            CompetitionStatus.Published,
            AutoApprove: true,
            CompetitionDeleted: false,
            Mode: GameMode.Ctf,
            Tracks: CompetitionTrackConfiguration.ToPersisted(configuration),
            TracksEnabled: false));

        var result = await new CreateTeam(store).ExecuteAsync(new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "no-track-selection",
            DateTimeOffset.UtcNow,
            TrackKey: null,
            TrackInvitationCode: "ignored"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.LastCreateCommand!.TrackKey).IsNull();
        await Assert.That(store.LastCreateCommand.TrackInvitationCode).IsEqualTo("ignored");
    }

    [Test]
    public async Task ReviewTeamRegistration_FinishedCompetitionRejectsBeforeWrite()
    {
        var store = new Store(new(CompetitionStatus.Finished, true, false));

        var result = await new ReviewTeamRegistration(store).ExecuteAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TeamRegistrationStatus.Approved);

        await Assert.That(result.FailureCode).IsEqualTo(TeamRegistrationFailure.CompetitionFinished);
        await Assert.That(store.ReviewWriteCount).IsEqualTo(0);
    }

    [Test]
    public async Task ReviewTeamRegistration_Allows_moderator_to_return_team_to_pending()
    {
        var store = new Store(new(CompetitionStatus.Published, true, false));

        var result = await new ReviewTeamRegistration(store).ExecuteAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TeamRegistrationStatus.Pending);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Status).IsEqualTo(TeamRegistrationStatus.Pending);
    }

    [Test]
    [Arguments(CompetitionStatus.Draft, false, false)]
    [Arguments(CompetitionStatus.Visible, false, true)]
    [Arguments(CompetitionStatus.Published, false, true)]
    [Arguments(CompetitionStatus.Running, false, false)]
    [Arguments(CompetitionStatus.Running, true, true)]
    [Arguments(CompetitionStatus.Paused, true, false)]
    [Arguments(CompetitionStatus.Finished, true, false)]
    public async Task Participant_organization_changes_follow_the_registration_window(
        CompetitionStatus status,
        bool allowWhileRunning,
        bool expected)
    {
        await Assert.That(ParticipantTeamMutationPolicy.CanChangeOrganization(
            status,
            allowWhileRunning)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetMyTeam_Includes_pending_and_rejected_registration_states()
    {
        var store = new Store(new(CompetitionStatus.Published, false, false));

        await new GetMyTeam(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid());

        await Assert.That(store.LastFindForUserIncludedPending).IsTrue();
    }

    private sealed class Store : ITeamRegistrationStore
    {
        private readonly TeamRegistrationPolicy policy;

        public Store(TeamRegistrationPolicy policy)
        {
            this.policy = policy.Tracks is null or { Count: 0 }
                ? policy with
                {
                    Tracks = CompetitionTrackConfiguration.ToPersisted(
                        CompetitionTrackConfiguration.DefaultFor(policy.Mode))
                }
                : policy;
        }

        public TeamRegistrationStatus? Status { get; private set; }
        public int ReviewWriteCount { get; private set; }
        public bool? LastFindForUserIncludedPending { get; private set; }
        public CreateTeamCommand? LastCreateCommand { get; private set; }
        public Task<TeamRegistrationPolicy?> GetPolicyAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult<TeamRegistrationPolicy?>(policy);
        public Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken cancellationToken)
        {
            LastCreateCommand = command;
            Status = status;
            TeamView team = new(
                Guid.NewGuid(),
                command.CompetitionId,
                command.Name,
                null,
                command.UserId,
                [command.UserId],
                status,
                false,
                false,
                command.RegisteredAt,
                command.TrackKey ?? CompetitionTrackConfiguration.DefaultTrackKey,
                command.TrackKey ?? CompetitionTrackConfiguration.DefaultTrackKey);
            return Task.FromResult(new TeamCreateStoreResult(team, null));
        }
        public Task<IReadOnlyList<TeamView>> ListAsync(Guid competitionId, bool includePending, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TeamView>>([]);
        public Task<TeamReviewStoreResult> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken cancellationToken)
        {
            ReviewWriteCount++;
            Status = status;
            return Task.FromResult(new TeamReviewStoreResult(true));
        }
        public Task<TeamView?> FindAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken cancellationToken) => Task.FromResult<TeamView?>(null);
        public Task<TeamView?> FindForUserAsync(Guid competitionId, Guid userId, bool includePending, CancellationToken cancellationToken)
        {
            LastFindForUserIncludedPending = includePending;
            return Task.FromResult<TeamView?>(null);
        }
        public Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<TeamUpdateStoreResult> UpdateAsync(UpdateTeamCommand command, CancellationToken cancellationToken) => Task.FromResult(new TeamUpdateStoreResult(null, TeamRegistrationFailure.TeamNotFound));
        public Task<TeamRegistrationFailure?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken) => Task.FromResult<TeamRegistrationFailure?>(null);
    }

    private static CompetitionTrackDefinition Track(
        string key,
        bool isDefault = false,
        bool publicSelectable = true,
        bool isInternal = false,
        string? invitationCode = null) => new(
        key,
        key,
        isDefault,
        publicSelectable,
        isInternal,
        EarnsScore: !isInternal,
        EarnsBlood: !isInternal,
        AffectsDynamicChallengeScore: !isInternal,
        VisibleOnLeaderboard: !isInternal,
        AffectsCompetitiveResults: !isInternal,
        InvitationCode: invitationCode);
}
