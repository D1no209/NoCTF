using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class GetAdminCompetitionEndpoint(
    GetAdminCompetition get,
    ICompetitionModerationAuthorizer authorizer,
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
        if (view is null)
            return TypedResults.NotFound();
        var competitionId = view.Id;
        var role = user.IsAdministrator || view.OwnerId == user.UserId
            ? CompetitionAdministrationRoleProtocol.Owner
            : await authorizer.CanModerateAsync(user.UserId, competitionId, ct)
                ? CompetitionAdministrationRoleProtocol.Manager
                : await authorizer.CanJudgeAsync(user.UserId, competitionId, ct)
                    ? CompetitionAdministrationRoleProtocol.Judge
                    : CompetitionAdministrationRoleProtocol.Observer;
        return TypedResults.Ok(CompetitionMapper.ToResponse(view) with
        {
            AdministrationRole = role
        });
    }
}
