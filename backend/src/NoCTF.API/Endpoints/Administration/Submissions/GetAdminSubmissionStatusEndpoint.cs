using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Teams.Moderation;
using NoCTF.API.Endpoints.Submissions;

namespace NoCTF.API.Endpoints.Administration.Submissions;

public sealed class GetAdminSubmissionStatusRequest
{
    public Guid CompetitionId { get; set; }
    public Guid SubmissionId { get; set; }
}

public sealed class GetAdminSubmissionStatusEndpoint(
    IAdminSubmissionStatusReader reader,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetAdminSubmissionStatusRequest, Results<Ok<AdminSubmissionStatusResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/submissions/{submissionId}");
        AuthSchemes("Bearer");
    }

    public override async Task<Results<Ok<AdminSubmissionStatusResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetAdminSubmissionStatusRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.SubmissionId = Route<Guid>("submissionId");
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, cancellationToken))
            return TypedResults.Forbid();
        var view = await reader.FindAsync(request.CompetitionId, request.SubmissionId, cancellationToken);
        return view is null ? TypedResults.NotFound() : TypedResults.Ok(SubmissionMapper.ToAdminStatusResponse(view));
    }
}
