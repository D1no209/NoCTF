using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class ExtendTeamRuntimeRequest
{
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ExtendTeamRuntimeValidator : Validator<ExtendTeamRuntimeRequest>
{
    public ExtendTeamRuntimeValidator() =>
        RuleFor(request => request.ExpiresAt).NotEmpty();
}

public sealed class ExtendTeamRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ExtendTeamRuntimeRequest,
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Description(builder => builder.WithName("AdminExtendTeamRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Extends a running team runtime.";
            summary.Description = "Uses the normal runtime state machine and does not bypass TTL or mode policy.";
        });
    }

    public override async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult, ForbidHttpResult>> ExecuteAsync(
        ExtendTeamRuntimeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var runtime = await runtimes.GetAsync(
            competitionId,
            Route<Guid>("runtimeInstanceId"),
            ct);
        if (runtime?.CompetitionChallengeId != Route<Guid>("competitionChallengeId")
            || runtime.TeamId != Route<Guid>("teamId")
            || runtime.ExpiresAt is null)
            return TypedResults.NotFound();
        return await AdminRuntimeMutation.ExecuteTeamAsync(
            runtimes,
            RuntimeAction.Extend,
            competitionId,
            runtime.CompetitionChallengeId.Value,
            runtime.TeamId,
            request.ExpiresAt - runtime.ExpiresAt.Value,
            timeProvider,
            ct);
    }
}
