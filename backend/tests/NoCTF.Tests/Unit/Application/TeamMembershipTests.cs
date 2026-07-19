using NoCTF.Application.Teams.Membership;

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
        var store = new Store { ResponseError = "invitation_expired" };
        var result = await new RespondToTeamInvitation(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), true, DateTimeOffset.UtcNow);
        await Assert.That(result.ErrorCode).IsEqualTo("invitation_expired");
    }

    private sealed class Store : ITeamMembershipStore
    {
        public string? ResponseError { get; set; }
        public Task<(TeamInvitationView? Invitation, string? Error)> InviteAsync(InviteTeamMemberCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<(TeamInvitationView?, string?)>((new(Guid.NewGuid(), command.CompetitionId, command.TeamId, command.InvitedUserId, command.ExpiresAt, command.Now), null));
        public Task<string?> RespondAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(ResponseError);
    }
}
