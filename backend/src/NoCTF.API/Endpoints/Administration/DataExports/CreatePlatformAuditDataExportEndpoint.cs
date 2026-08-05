using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.DataExports;
using NoCTF.Domain.DataExports;

namespace NoCTF.API.Endpoints.Administration.DataExports;

public sealed class CreatePlatformAuditDataExportEndpoint(
    RequestDataExport requestExport,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Accepted<DataExportResponse>, Conflict<DataExportResponse>,
            ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/audit-logs/data-exports");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminCreatePlatformAuditDataExport"));
        Summary(summary =>
        {
            summary.Summary = "Queues a complete platform audit export.";
            summary.Description =
                "Exports projections of immutable competition events and user lifecycle audits as NDJSON.";
        });
    }

    public override async Task<
        Results<Accepted<DataExportResponse>, Conflict<DataExportResponse>,
            ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var result = await requestExport.ExecuteAsync(new RequestDataExportCommand(
            DataExportScope.PlatformAudit,
            null,
            user.UserId,
            user.IsAdministrator,
            user.IsHuman,
            false,
            null), cancellationToken);
        if (result.Failure == RequestDataExportFailure.ActiveExportExists
            && result.Export is not null)
        {
            return TypedResults.Conflict(CreateCompetitionDataExportEndpoint.Map(result.Export));
        }
        if (result.Failure == RequestDataExportFailure.Forbidden)
            return TypedResults.Forbid();
        if (result.Failure is not null || result.Export is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The platform audit export request is invalid.",
                detail: result.Failure?.ToString());
        }
        return TypedResults.Accepted<DataExportResponse>(
            (string?)null,
            CreateCompetitionDataExportEndpoint.Map(result.Export));
    }
}
