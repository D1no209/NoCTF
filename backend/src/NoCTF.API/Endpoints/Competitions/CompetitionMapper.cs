using NoCTF.Application.Competitions.Management;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Competitions;

[Mapper]
internal static partial class CompetitionMapper
{
    public static partial CreateCompetitionCommand ToCommand(CreateCompetitionRequest request, Guid ownerId, DateTimeOffset createdAt);
    public static partial CompetitionResponse ToResponse(CompetitionView view);
}
