using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class ResetSharedRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/reset");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Queues a KoH shared runtime replacement.");
    }

    public override Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes, authorizer, user, RuntimeAction.Reset,
            Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            teamId: null, extension: null, ct);
}
