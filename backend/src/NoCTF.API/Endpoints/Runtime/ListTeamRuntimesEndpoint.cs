using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class ListTeamRuntimesRequest : PaginationRequest
{
    public ListTeamRuntimesRequest() => Desc = true;
}

public sealed class ListTeamRuntimesValidator : Validator<ListTeamRuntimesRequest>
{
    public ListTeamRuntimesValidator() => PaginationRules.Add(this);
}

public sealed record TeamRuntimeListItemResponse(string ChallengeTitle, RuntimeResponse Runtime);

public sealed class ListTeamRuntimesEndpoint(
    ListTeamRuntimes runtimes,
    IUserContext user)
    : Endpoint<ListTeamRuntimesRequest,
        Results<Ok<ArrayResult<TeamRuntimeListItemResponse>>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/teams/me/runtimes");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("ListMyTeamRuntimes"));
        Summary(summary => summary.Summary = "Lists the current team's active challenge runtimes.");
    }

    public override async Task<Results<Ok<ArrayResult<TeamRuntimeListItemResponse>>, NotFound>> ExecuteAsync(
        ListTeamRuntimesRequest request,
        CancellationToken ct)
    {
        var page = await runtimes.ExecuteAsync(
            Route<Guid>("competitionId"), user.UserId,
            request.Offset, request.Limit, request.Desc, ct);
        if (page is null) return TypedResults.NotFound();
        return TypedResults.Ok(new ArrayResult<TeamRuntimeListItemResponse>(
            page.Items.Select(item => new TeamRuntimeListItemResponse(
                item.ChallengeTitle,
                RuntimeEndpointMapping.ToResponse(item.Runtime, HttpContext.Request))).ToArray(),
            page.Total));
    }
}
