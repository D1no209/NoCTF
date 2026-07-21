using NoCTF.Application.Competitions.Configuration;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[Mapper]
internal static partial class CompetitionConfigurationMapper
{
    public static partial CompetitionConfigurationResponse ToResponse(CompetitionConfigurationView view);
}
