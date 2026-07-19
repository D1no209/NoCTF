using NoCTF.Application.Challenges.Configuration;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.Challenges;

[Mapper]
internal static partial class ChallengeConfigurationMapper
{
    public static partial ChallengeConfigurationResponse ToResponse(ChallengeConfigurationView view);
}
