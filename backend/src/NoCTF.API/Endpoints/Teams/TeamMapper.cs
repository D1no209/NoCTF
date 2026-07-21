using NoCTF.Application.Teams.Registration;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Teams;

[Mapper]
internal static partial class TeamMapper
{
    public static partial CreateTeamCommand ToCommand(CreateTeamRequest request, Guid userId, DateTimeOffset registeredAt);
    public static partial TeamResponse ToResponse(TeamView view);
    public static partial UpdateTeamCommand ToCommand(UpdateTeamRequest request);
}
