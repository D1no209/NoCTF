using NoCTF.Application.Competitions.Collaborators;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[Mapper]
internal static partial class CompetitionCollaboratorMapper
{
    public static partial AddCompetitionCollaboratorCommand ToCommand(AddCompetitionCollaboratorRequest request, DateTimeOffset addedAt);
    public static partial CompetitionCollaboratorResponse ToResponse(CompetitionCollaboratorView view);
}
