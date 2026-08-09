using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class GetAdminCompetitionEndpoint(
    GetAdminCompetition get,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<CompetitionResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Gets an administratively visible competition.";
            summary.Description = "Returns draft or public competition metadata when the caller has resource access.";
        });
    }

    public override async Task<Results<Ok<CompetitionResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            includeDeleted: true,
            ct: ct);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CompetitionMapper.ToResponse(view));
    }
}
