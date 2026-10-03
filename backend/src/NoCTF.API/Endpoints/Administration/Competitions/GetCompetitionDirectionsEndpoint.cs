using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Directions;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class GetCompetitionDirectionsRequest
{
    public Guid CompetitionId { get; set; }
}
public sealed record CompetitionDirectionResponse(Guid Id, string Name, string Icon);
public sealed record CompetitionDirectionsResponse(IReadOnlyList<CompetitionDirectionResponse> Items);

public sealed class GetCompetitionDirectionsEndpoint(ManageCompetitionDirections directions,
    ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<GetCompetitionDirectionsRequest, Results<Ok<CompetitionDirectionsResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/directions");
        AuthSchemes("Bearer");
        Summary(summary => { summary.Summary = "Lists competition challenge directions."; summary.Description = "Returns this competition’s ordered direction names and Lucide icon suffixes to authorized observers."; });
        Description(builder => builder.WithName("AdminGetCompetitionDirections"));
    }
    public override async Task<Results<Ok<CompetitionDirectionsResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(GetCompetitionDirectionsRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var items = await directions.ListAsync(request.CompetitionId, ct);
        return items is null ? TypedResults.NotFound() : TypedResults.Ok(new CompetitionDirectionsResponse(
            items.Select(item => new CompetitionDirectionResponse(item.Id, item.Name, item.Icon)).ToArray()));
    }
}
