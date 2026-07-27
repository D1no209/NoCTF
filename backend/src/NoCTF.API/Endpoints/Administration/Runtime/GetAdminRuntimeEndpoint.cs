using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class GetAdminRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<AdminRuntimeResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetRuntime"));
        Summary(summary =>
        {
            summary.Summary = "Gets an administrative runtime view.";
            summary.Description = "Includes provider receipt and internal failure diagnostics hidden from players.";
        });
    }

    public override async Task<Results<Ok<AdminRuntimeResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await runtimes.GetAsync(
            competitionId, Route<Guid>("runtimeInstanceId"), ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(AdminRuntimeMapping.ToResponse(result));
    }
}
