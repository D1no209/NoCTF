using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class StopSharedRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/stop");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Description(builder => builder.WithName("AdminStopSharedRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Queues a KoH shared runtime stop.";
            summary.Description = "Stops the shared hill through the durable runtime cleanup state machine.";
        });
    }

    public override Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes, authorizer, user, RuntimeAction.Stop,
            Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            teamId: null, extension: null, timeProvider, ct);
}
