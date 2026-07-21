using NoCTF.Application.Challenges.Flags;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.Challenges;

[Mapper]
internal static partial class ChallengeFlagMapper
{
    public static partial CreateChallengeFlagCommand ToCommand(
        CreateChallengeFlagRequest request,
        DateTimeOffset createdAt);

    public static partial UpdateChallengeFlagCommand ToCommand(
        UpdateChallengeFlagRequest request,
        DateTimeOffset updatedAt);

    public static partial ChallengeFlagSecretResponse ToResponse(ChallengeFlagView view);
    private static partial IReadOnlyList<ChallengeFlagSecretResponse> ToResponses(
        IReadOnlyList<ChallengeFlagView> views);

    public static ChallengeFlagSecretListResponse ToListResponse(IReadOnlyList<ChallengeFlagView> views) =>
        new(ToResponses(views));
}
