using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class ExportRuntimeTrafficCapturesRequest
{
    public IReadOnlyList<Guid> RuntimeInstanceIds { get; set; } = [];
}

[NJsonSchema.Annotations.JsonSchema(NJsonSchema.JsonObjectType.String, Format = "binary")]
public sealed class RuntimeTrafficCaptureArchiveResponse;

public sealed class ExportRuntimeTrafficCapturesValidator
    : Validator<ExportRuntimeTrafficCapturesRequest>
{
    public ExportRuntimeTrafficCapturesValidator() =>
        RuleFor(request => request.RuntimeInstanceIds)
            .NotEmpty()
            .Must(ids => ids.Count <= 100 && ids.Distinct().Count() == ids.Count)
            .WithMessage("Select between 1 and 100 distinct Runtime captures.");
}

public sealed class ExportRuntimeTrafficCapturesEndpoint(
    ManageRuntimeTrafficCaptures captures,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ExportRuntimeTrafficCapturesRequest,
        Results<PushStreamHttpResult, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/traffic-captures/export");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("AdminExportRuntimeTrafficCaptures")
            .Produces<RuntimeTrafficCaptureArchiveResponse>(
                StatusCodes.Status200OK,
                "application/zip"));
        Summary(summary => summary.Summary = "Exports selected Runtime captures as ZIP.");
    }

    public override async Task<Results<PushStreamHttpResult, NotFound,
        ForbidHttpResult>> ExecuteAsync(
        ExportRuntimeTrafficCapturesRequest request,
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
        var capture = await captures.ExportAsync(
            competitionId,
            request.RuntimeInstanceIds,
            cancellationToken);
        return capture is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(
                output => capture.WriteToAsync(output, HttpContext.RequestAborted),
                capture.ContentType,
                capture.FileName);
    }
}
