using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class DownloadRuntimeTrafficCaptureRequest
{
    public Guid CompetitionId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
}

[NJsonSchema.Annotations.JsonSchema(NJsonSchema.JsonObjectType.String, Format = "binary")]
public sealed class RuntimeTrafficCaptureBinaryResponse;

public sealed class DownloadRuntimeTrafficCaptureEndpoint(
    ManageRuntimeTrafficCaptures captures,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<DownloadRuntimeTrafficCaptureRequest,
        Results<FileStreamHttpResult, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/traffic-captures/{runtimeInstanceId}/file");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("AdminDownloadRuntimeTrafficCapture")
            .Produces<RuntimeTrafficCaptureBinaryResponse>(
                StatusCodes.Status200OK,
                "application/vnd.tcpdump.pcap"));
        Summary(summary => summary.Summary = "Downloads one Runtime PCAPNG capture.");
    }

    public override async Task<Results<FileStreamHttpResult, NotFound,
        ForbidHttpResult>> ExecuteAsync(
        DownloadRuntimeTrafficCaptureRequest request,
        CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(
                user.UserId,
                competitionId,
                cancellationToken))
            return TypedResults.Forbid();
        var capture = await captures.OpenAsync(
            competitionId,
            Route<Guid>("runtimeInstanceId"),
            cancellationToken);
        return capture is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(
                capture.Content,
                capture.ContentType,
                capture.FileName);
    }
}
