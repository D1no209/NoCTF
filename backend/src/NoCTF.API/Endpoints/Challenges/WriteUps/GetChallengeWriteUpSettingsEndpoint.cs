using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class GetChallengeWriteUpSettingsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
}
public sealed record ChallengeWriteUpSettingsResponse(WriteUpSettingsView Settings, Guid ConcurrencyStamp);
public sealed class GetChallengeWriteUpSettingsEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<GetChallengeWriteUpSettingsRequest, Results<Ok<ChallengeWriteUpSettingsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/writeup-settings"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetChallengeWriteUpSettings"));
        Summary(x => x.Summary = "Reads the independent single-challenge WriteUp policy.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpSettingsResponse>, NotFound>> ExecuteAsync(GetChallengeWriteUpSettingsRequest request, CancellationToken ct)
    {
        var result = await writeUps.GetSettingsAsync(request.CompetitionId, request.CompetitionChallengeId, user.UserId, clock.GetUtcNow(), ct);
        return result.Settings is null ? TypedResults.NotFound() : TypedResults.Ok(new ChallengeWriteUpSettingsResponse(result.Settings, result.ConcurrencyStamp!.Value));
    }
}
