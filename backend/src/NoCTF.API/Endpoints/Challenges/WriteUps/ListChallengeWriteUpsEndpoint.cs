using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class ListChallengeWriteUpsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public bool Staff { get; set; }
}
public sealed record ChallengeWriteUpVersionResponse(Guid Id, int Number,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<WriteUpFormat>))] WriteUpFormat Format,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<WriteUpVersionState>))] WriteUpVersionState State,
    Guid ConcurrencyStamp, Guid ActorUserId, DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt, string? ReviewReason,
    string? ActorDisplayName, DateTimeOffset? PublishedAt);
public sealed record ChallengeWriteUpResponse(Guid Id, Guid CompetitionChallengeId, string ChallengeTitle,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<WriteUpSource>))] WriteUpSource Source,
    Guid? TeamId, string AuthorName, Guid ConcurrencyStamp, Guid? PublishedVersionId, DateTimeOffset UpdatedAt,
    ChallengeWriteUpVersionResponse? Draft, ChallengeWriteUpVersionResponse? Submitted,
    ChallengeWriteUpVersionResponse? Published, IReadOnlyList<ChallengeWriteUpVersionResponse> Versions, int ViewedTeamCount);
public sealed record ChallengeWriteUpListResponse(WriteUpAccessView Access, IReadOnlyList<ChallengeWriteUpResponse> Items);
public sealed record ChallengeWriteUpFailureResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeWriteUpFailure>))] ChallengeWriteUpFailure Code)
{
    public string MessageKey => $"challengeWriteUp.error.{Code}";
}
internal static class ChallengeWriteUpProtocol
{
    public static ChallengeWriteUpResponse Map(ChallengeWriteUpView value)
    {
        static ChallengeWriteUpVersionResponse? Version(WriteUpVersionView? v) => v is null ? null :
            new(v.Id, v.Number, v.Format, v.State, v.ConcurrencyStamp, v.ActorUserId, v.UpdatedAt, v.SubmittedAt, v.ReviewReason,
                v.ActorDisplayName, v.PublishedAt);
        return new(value.Id, value.CompetitionChallengeId, value.ChallengeTitle, value.Source, value.TeamId,
            value.AuthorName, value.ConcurrencyStamp, value.PublishedVersionId, value.UpdatedAt,
            Version(value.Draft), Version(value.Submitted), Version(value.Published), value.Versions.Select(x => Version(x)!).ToArray(), value.ViewedTeamCount);
    }
    public static Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult, Conflict<ChallengeWriteUpFailureResponse>,
        UnprocessableEntity<ChallengeWriteUpFailureResponse>> Mutation(WriteUpMutationResult result) => result.Failure switch
    {
        null => TypedResults.Ok(Map(result.WriteUp!)),
        ChallengeWriteUpFailure.NotFound => TypedResults.NotFound(),
        ChallengeWriteUpFailure.Forbidden => TypedResults.Forbid(),
        ChallengeWriteUpFailure.InvalidContent => TypedResults.UnprocessableEntity(new ChallengeWriteUpFailureResponse(result.Failure.Value)),
        _ => TypedResults.Conflict(new ChallengeWriteUpFailureResponse(result.Failure.Value))
    };
}
public sealed class ListChallengeWriteUpsEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<ListChallengeWriteUpsRequest, Results<Ok<ChallengeWriteUpListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListChallengeWriteUps"));
        Summary(x => x.Summary = "Lists permitted challenge WriteUp metadata without revealing content.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpListResponse>, NotFound>> ExecuteAsync(ListChallengeWriteUpsRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await writeUps.ListAsync(request.CompetitionId, request.CompetitionChallengeId, user.UserId, request.Staff, clock.GetUtcNow(), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new ChallengeWriteUpListResponse(result.Access, result.Items.Select(ChallengeWriteUpProtocol.Map).ToArray()));
    }
}
