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
    : EndpointWithoutRequest<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/reset");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminResetSharedRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Queues a KoH shared runtime replacement.";
            summary.Description = "Replaces the shared hill through the normal generation-fenced runtime state machine.";
        });
    }

    public override Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes, authorizer, user, RuntimeAction.Reset,
            Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            teamId: null, extension: null, ct);
}
