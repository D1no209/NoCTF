using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Pagination;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class ListAdminTeamsRequest : SearchRequest
{
}

public sealed class ListAdminTeamsValidator : Validator<ListAdminTeamsRequest>
{
    public ListAdminTeamsValidator() => PaginationRules.AddSearch(this);
}

public sealed class ListAdminTeamsEndpoint(
    ListCompetitionTeams list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    LinkGenerator links)
    : Endpoint<ListAdminTeamsRequest, Results<Ok<TeamListResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/teams");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListTeams"));
        Summary(summary =>
        {
            summary.Summary = "Lists all competition teams for management.";
            summary.Description = "Returns registration and moderation state to authorized competition observers.";
        });
    }

    public override async Task<Results<Ok<TeamListResponse>, ForbidHttpResult>> ExecuteAsync(
        ListAdminTeamsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var page = await list.ExecutePageAsync(new(
            competitionId,
            IncludePending: true,
            IncludeInternal: true,
            PaginationRules.Normalize(request.Keyword),
            request.Offset,
            request.Limit,
            request.Desc), ct);
        return TypedResults.Ok(new TeamListResponse(
            page.Items.Select(item => TeamMapper.ToResponse(item, links, HttpContext)).ToArray(),
            page.Total));
    }
}
