using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed record LiveSoloQuestionGroupEntryContract(Guid CompetitionChallengeId, int? OpenOffsetSeconds);
public sealed record LiveSoloQuestionGroupResponse(Guid Id, string Name, bool Reserve, int? LimitSeconds, Guid ConcurrencyStamp,
    IReadOnlyList<LiveSoloQuestionGroupEntryContract> Questions);
public sealed record LiveSoloQuestionGroupsResponse(IReadOnlyList<LiveSoloQuestionGroupResponse> Items);
internal static class LiveSoloGroupProtocol
{
    public static LiveSoloQuestionGroupResponse Group(LiveSoloQuestionGroupView value) => new(value.Id, value.Name, value.Reserve,
        value.LimitSeconds, value.ConcurrencyStamp, value.Questions.Select(x => new LiveSoloQuestionGroupEntryContract(x.CompetitionChallengeId, x.OpenOffsetSeconds)).ToArray());
}
public sealed class ListLiveSoloQuestionGroupsRequest { public Guid CompetitionId { get; set; } }
public sealed class ListLiveSoloQuestionGroupsEndpoint(ILiveSoloMatchStore matches, IUserContext user)
    : Endpoint<ListLiveSoloQuestionGroupsRequest, Results<Ok<LiveSoloQuestionGroupsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/question-groups"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloQuestionGroups"));
        Summary(x => x.Summary = "Lists question-pool scheduling metadata for authorized staff only.");
    }
    public override async Task<Results<Ok<LiveSoloQuestionGroupsResponse>, NotFound>> ExecuteAsync(ListLiveSoloQuestionGroupsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await matches.GroupsAsync(req.CompetitionId, user.UserId, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloQuestionGroupsResponse(result.Select(LiveSoloGroupProtocol.Group).ToArray()));
    }
}
