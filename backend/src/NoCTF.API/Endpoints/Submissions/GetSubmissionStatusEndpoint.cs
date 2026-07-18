using FastEndpoints;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class GetSubmissionStatusEndpoint(
    ISubmissionStatusReader statusReader,
    IUserContext userContext) : Endpoint<GetSubmissionStatusRequest, SubmissionStatusResponse>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/submissions/{submissionId}");
        AuthSchemes("Bearer");
    }

    public override async Task HandleAsync(GetSubmissionStatusRequest request, CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.SubmissionId = Route<Guid>("submissionId");
        var result = await statusReader.FindAsync(
            request.CompetitionId,
            request.SubmissionId,
            userContext.UserId,
            cancellationToken);
        if (result is null)
        {
            await HttpContext.Response.SendNotFoundAsync(cancellationToken);
            return;
        }
        await HttpContext.Response.SendAsync<SubmissionStatusResponse>(new(
            result.SubmissionId,
            result.Kind,
            result.Outcome,
            result.ReceivedAt,
            result.CompletedAt,
            result.ErrorCode), StatusCodes.Status200OK, null, cancellationToken);
    }
}
