using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.DataExports;
using NoCTF.Domain.DataExports;

namespace NoCTF.API.Endpoints.Administration.DataExports;

public sealed class ListCompetitionDataExportsEndpoint(
    ListDataExports list,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Ok<DataExportListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/data-exports");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionDataExports"));
        Summary(summary =>
        {
            summary.Summary = "Lists recent competition archive jobs.";
            summary.Description =
                "Administrator, competition owner, and manager only. Administrators can inspect every recent job; other callers see only their own jobs.";
        });
    }

    public override async Task<
        Results<Ok<DataExportListResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var result = await list.ExecuteAsync(new ListDataExportsQuery(
            DataExportScope.CompetitionArchive,
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator), cancellationToken);
        if (result.Failure == ListDataExportsFailure.SubjectNotFound)
            return TypedResults.NotFound();
        if (result.Failure == ListDataExportsFailure.Forbidden)
            return TypedResults.Forbid();
        return TypedResults.Ok(new DataExportListResponse(
            (result.Items ?? []).Select(CreateCompetitionDataExportEndpoint.Map).ToArray()));
    }
}
