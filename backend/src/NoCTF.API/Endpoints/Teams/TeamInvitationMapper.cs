using NoCTF.Application.Teams.Membership;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Teams;

[Mapper]
internal static partial class TeamInvitationMapper
{
    public static partial InviteTeamMemberCommand ToCommand(InviteTeamMemberRequest request, Guid actorId, DateTimeOffset now, DateTimeOffset expiresAt);
    public static partial TeamInvitationResponse ToResponse(TeamInvitationView view);
}
