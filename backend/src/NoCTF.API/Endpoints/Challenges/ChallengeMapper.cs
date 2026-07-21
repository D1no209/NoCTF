using NoCTF.Application.Challenges.Management;
using Riok.Mapperly.Abstractions;
namespace NoCTF.API.Endpoints.Challenges;
[Mapper]
internal static partial class ChallengeMapper
{
    public static partial CreateChallengeCommand ToCommand(CreateChallengeRequest request, DateTimeOffset createdAt);
    public static partial UpdateChallengeCommand ToCommand(UpdateChallengeRequest request, DateTimeOffset updatedAt);
    public static partial ChallengeResponse ToResponse(ChallengeView view);
    private static partial IReadOnlyList<ChallengeResponse> ToResponses(IReadOnlyList<ChallengeView> views);

    public static ChallengeListResponse ToListResponse(IReadOnlyList<ChallengeView> views) =>
        new(ToResponses(views));
}
