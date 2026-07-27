using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record AdminCompetitionListResponse(IReadOnlyList<CompetitionResponse> Items);

public sealed class ListAdminCompetitionsEndpoint(
    ListAdminCompetitions list,
    IUserContext user)
    : EndpointWithoutRequest<Ok<AdminCompetitionListResponse>>
{
    public override void Configure()
    {
        Get("/admin/competitions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitions"));
        Summary(summary =>
        {
            summary.Summary = "Lists competitions visible to the current administrator.";
            summary.Description = "Resource permissions, not the /admin path alone, determine visibility.";
        });
    }

    public override async Task<Ok<AdminCompetitionListResponse>> ExecuteAsync(CancellationToken ct)
    {
        var items = await list.ExecuteAsync(user.UserId, user.IsAdministrator, ct);
        return TypedResults.Ok(new AdminCompetitionListResponse(
            items.Select(CompetitionMapper.ToResponse).ToArray()));
    }
}
