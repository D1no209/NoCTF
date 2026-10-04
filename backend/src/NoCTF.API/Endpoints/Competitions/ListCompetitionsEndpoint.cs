using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Access;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Competitions;

public sealed record CompetitionListResponse(IReadOnlyList<CompetitionResponse> Items);

public sealed class ListCompetitionsEndpoint(
    ListCompetitions list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Ok<CompetitionListResponse>>
{
    public override void Configure() {
        Summary(summary =>
        {
            summary.Summary = "Lists competitions visible to the caller using cursor pagination.";
            summary.Description = summary.Summary;
        });
 Get("/competitions"); AllowAnonymous(); }

    public override async Task<Ok<CompetitionListResponse>> ExecuteAsync(CancellationToken ct)
    {
        var items = await list.ExecuteAsync(false, ct);
        var now = timeProvider.GetUtcNow();
        var visible = await CompetitionAudiencePolicy.FilterCatalogAsync(
            items,
            user.UserId,
            user.IsAdministrator,
            authorizer,
            ct);
        HttpContext.Response.Headers.CacheControl = "private,no-store";
        HttpContext.Response.Headers.Vary = "Authorization";
        return TypedResults.Ok(new CompetitionListResponse(
            visible.Select(item => CompetitionMapper.ToResponse(item, now)).ToList()));
    }

}
