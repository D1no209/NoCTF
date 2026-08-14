using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Tests.Unit.Application;

public class TeamRegistrationTests
{
    [Test]
    public async Task CreateTeam_AutoApproveCreatesCaptainTeam()
    {
        var store = new Store(new(CompetitionStatus.Published, true, false));
        var command = new CreateTeamCommand(Guid.NewGuid(), Guid.NewGuid(), "alpha", DateTimeOffset.UtcNow, "default");

        var result = await new CreateTeam(store).ExecuteAsync(command);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Status).IsEqualTo(TeamRegistrationStatus.Approved);
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
        await Assert.That(store.Status).IsEqualTo(TeamRegistrationStatus.Pending);
    }

    [Test]
    public async Task CreateTeam_Requires_an_explicit_publicly_selectable_track()
    {
        var configuration = new CompetitionTrackConfiguration(1,
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
            TrackConfigurationJson: CompetitionTrackConfiguration.Serialize(configuration));
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
    public async Task ReviewTeamRegistration_FinishedCompetitionRejectsBeforeWrite()
    {
        var store = new Store(new(CompetitionStatus.Finished, true, false));

        var result = await new ReviewTeamRegistration(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), true);

        await Assert.That(result.FailureCode).IsEqualTo(TeamRegistrationFailure.CompetitionFinished);
        await Assert.That(store.ReviewWriteCount).IsEqualTo(0);
    }

    private sealed class Store(TeamRegistrationPolicy policy) : ITeamRegistrationStore
    {
        public TeamRegistrationStatus? Status { get; private set; }
        public int ReviewWriteCount { get; private set; }
        public Task<TeamRegistrationPolicy?> GetPolicyAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult<TeamRegistrationPolicy?>(policy);
        public Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken cancellationToken)
        {
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
            return Task.FromResult(new TeamReviewStoreResult(true));
        }
        public Task<TeamView?> FindAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken cancellationToken) => Task.FromResult<TeamView?>(null);
        public Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<TeamUpdateStoreResult> UpdateAsync(UpdateTeamCommand command, CancellationToken cancellationToken) => Task.FromResult(new TeamUpdateStoreResult(null, TeamRegistrationFailure.TeamNotFound));
        public Task<TeamRegistrationFailure?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken) => Task.FromResult<TeamRegistrationFailure?>(null);
    }

    private static CompetitionTrackDefinition Track(
        string key,
        bool isDefault = false,
        bool publicSelectable = true,
        bool isInternal = false) => new(
        key,
        key,
        isDefault,
        publicSelectable,
        isInternal,
        EarnsScore: !isInternal,
        EarnsBlood: !isInternal,
        AffectsDynamicChallengeScore: !isInternal,
        VisibleOnLeaderboard: !isInternal,
        AffectsCompetitiveResults: !isInternal);
}
