using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class DeleteRuntimeTrafficCaptureRequest
{
    public Guid CompetitionId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
}

public sealed record RuntimeTrafficCaptureConflictResponse(string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(ApiMessageId.RuntimeTrafficCaptureConflict, Detail);
    public string MessageKey => ApiMessages.Key(ApiMessageId.RuntimeTrafficCaptureConflict);
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class DeleteRuntimeTrafficCaptureEndpoint(
    ManageRuntimeTrafficCaptures captures,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<DeleteRuntimeTrafficCaptureRequest,
        Results<NoContent, NotFound, ForbidHttpResult,
            Conflict<RuntimeTrafficCaptureConflictResponse>>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/traffic-captures/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteRuntimeTrafficCapture"));
        Summary(summary => { summary.Summary = "Deletes one terminal Runtime capture."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult,
        Conflict<RuntimeTrafficCaptureConflictResponse>>> ExecuteAsync(
        DeleteRuntimeTrafficCaptureRequest request,
        CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(
                user.UserId,
                competitionId,
                cancellationToken))
            return TypedResults.Forbid();
        return await captures.DeleteAsync(
            competitionId,
            Route<Guid>("runtimeInstanceId"),
            user.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken) switch
        {
            RuntimeTrafficCaptureDeleteState.Deleted => TypedResults.NoContent(),
            RuntimeTrafficCaptureDeleteState.NotFound => TypedResults.NotFound(),
            RuntimeTrafficCaptureDeleteState.RuntimeActive => TypedResults.Conflict(
                new RuntimeTrafficCaptureConflictResponse(
                    "Traffic captures can be deleted only after the Runtime is terminal.")),
            _ => TypedResults.Conflict(new RuntimeTrafficCaptureConflictResponse(
                "Traffic capture deletion could not be completed."))
        };
    }
}
