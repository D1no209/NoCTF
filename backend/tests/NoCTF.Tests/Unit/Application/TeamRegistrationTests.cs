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
        var command = new CreateTeamCommand(Guid.NewGuid(), Guid.NewGuid(), "alpha", null, DateTimeOffset.UtcNow);

        var result = await new CreateTeam(store).ExecuteAsync(command);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Status).IsEqualTo(TeamRegistrationStatus.Approved);
        await Assert.That(result.Value!.CaptainId).IsEqualTo(command.UserId);
    }

    [Test]
    public async Task CreateTeam_RunningCompetitionRejectsRegistration()
    {
        var store = new Store(new(CompetitionStatus.Running, true, false));
        var result = await new CreateTeam(store).ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), "alpha", null, DateTimeOffset.UtcNow));
        await Assert.That(result.ErrorCode).IsEqualTo("registration_closed");
    }

    [Test]
    public async Task ReviewTeamRegistration_FinishedCompetitionRejectsBeforeWrite()
    {
        var store = new Store(new(CompetitionStatus.Finished, true, false));

        var result = await new ReviewTeamRegistration(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), true);

        await Assert.That(result.ErrorCode).IsEqualTo("competition_finished");
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
            TeamView team = new(Guid.NewGuid(), command.CompetitionId, command.Name, command.AvatarUrl, command.UserId, status, false, command.RegisteredAt);
            return Task.FromResult(new TeamCreateStoreResult(team, null));
        }
        public Task<IReadOnlyList<TeamView>> ListAsync(Guid competitionId, bool includePending, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TeamView>>([]);
        public Task<bool?> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken cancellationToken)
        {
            ReviewWriteCount++;
            return Task.FromResult<bool?>(true);
        }
        public Task<TeamView?> FindAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken cancellationToken) => Task.FromResult<TeamView?>(null);
        public Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<TeamView?> UpdateAsync(UpdateTeamCommand command, CancellationToken cancellationToken) => Task.FromResult<TeamView?>(null);
        public Task<string?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
