using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.DataExports;
using NoCTF.Domain.DataExports;

namespace NoCTF.API.Endpoints.Administration.DataExports;

public sealed class ListPlatformAuditDataExportsEndpoint(
    ListDataExports list,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<DataExportListResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/platform/audit-logs/data-exports");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminListPlatformAuditDataExports"));
        Summary(summary =>
        {
            summary.Summary = "Lists recent platform audit export jobs.";
            summary.Description =
                "Administrator only. Returns the latest durable platform audit export jobs and their retention state.";
        });
    }

    public override async Task<Results<Ok<DataExportListResponse>, ForbidHttpResult>>
        ExecuteAsync(CancellationToken cancellationToken)
    {
        var result = await list.ExecuteAsync(new ListDataExportsQuery(
            DataExportScope.PlatformAudit,
            null,
            user.UserId,
            user.IsAdministrator), cancellationToken);
        if (result.Failure is not null)
            return TypedResults.Forbid();
        return TypedResults.Ok(new DataExportListResponse(
            (result.Items ?? []).Select(CreateCompetitionDataExportEndpoint.Map).ToArray()));
    }
}
