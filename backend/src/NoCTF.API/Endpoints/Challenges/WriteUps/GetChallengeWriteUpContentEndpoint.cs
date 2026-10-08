using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class GetChallengeWriteUpContentRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid VersionId { get; set; }
    public bool Staff { get; set; }
}
public sealed record ChallengeWriteUpContentResponse(Guid VersionId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<WriteUpFormat>))] WriteUpFormat Format, string? Markdown, string? FileName);
public sealed class GetChallengeWriteUpContentEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<GetChallengeWriteUpContentRequest, Results<Ok<ChallengeWriteUpContentResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/versions/{versionId}"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetChallengeWriteUpContent"));
        Summary(x => x.Summary = "Reads content only for the author, authorized staff, unlocked team or post-competition participant.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpContentResponse>, NotFound>> ExecuteAsync(GetChallengeWriteUpContentRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await writeUps.ReadContentAsync(request.CompetitionId, request.CompetitionChallengeId, request.VersionId,
            user.UserId, request.Staff, clock.GetUtcNow(), ct);
        return result.Failure is not null ? TypedResults.NotFound() : TypedResults.Ok(new ChallengeWriteUpContentResponse(result.VersionId,
            result.Format, result.Markdown, result.File?.FileName));
    }
}
