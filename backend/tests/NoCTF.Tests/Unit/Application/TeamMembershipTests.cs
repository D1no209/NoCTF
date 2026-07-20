using NoCTF.Application.Teams.Membership;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class TeamMembershipTests
{
    [Test]
    public async Task Invite_RequiresFutureExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var result = await new InviteTeamMember(new Store()).ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now, now));
        await Assert.That(result.ErrorCode).IsEqualTo("invalid_invitation_expiry");
    }

    [Test]
    public async Task Respond_PropagatesExpiredInvitation()
    {
        var store = new Store { ResponseFailure = TeamMembershipFailure.InvitationExpired };
        var result = await new RespondToTeamInvitation(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), true, DateTimeOffset.UtcNow);
        await Assert.That(result.ErrorCode).IsEqualTo("invitation_expired");
    }

    [Test]
    [Arguments(CompetitionStatus.Running, false, false)]
    [Arguments(CompetitionStatus.Paused, false, false)]
    [Arguments(CompetitionStatus.Running, true, true)]
    [Arguments(CompetitionStatus.Paused, true, true)]
    [Arguments(CompetitionStatus.Finished, false, true)]
    [Arguments(CompetitionStatus.Finished, true, true)]
    public async Task InvitationResponsePolicy_PreservesRejectButLocksMembershipChanges(
        CompetitionStatus status,
        bool accept,
        bool expectedLocked)
    {
        await Assert.That(TeamMembershipPolicy.IsInvitationResponseLocked(status, accept))
            .IsEqualTo(expectedLocked);
    }

    private sealed class Store : ITeamMembershipStore
    {
        public TeamMembershipFailure? ResponseFailure { get; set; }
        public Task<InviteTeamMemberStoreResult> InviteAsync(InviteTeamMemberCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new InviteTeamMemberStoreResult(new(Guid.NewGuid(), command.CompetitionId, command.TeamId, command.InvitedUserId, command.ExpiresAt, command.Now)));
        public Task<TeamMembershipFailure?> RespondAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(ResponseFailure);
        public Task<TeamMembershipFailure?> RemoveMemberAsync(Guid competitionId, Guid teamId, Guid targetUserId, Guid actorId, CancellationToken cancellationToken) => Task.FromResult<TeamMembershipFailure?>(null);
        public Task<TeamMembershipFailure?> LeaveAsync(Guid competitionId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<TeamMembershipFailure?>(null);
        public Task<TeamMembershipFailure?> TransferCaptainAsync(Guid competitionId, Guid teamId, Guid actorId, Guid newCaptainId, CancellationToken cancellationToken) => Task.FromResult<TeamMembershipFailure?>(null);
    }
}
