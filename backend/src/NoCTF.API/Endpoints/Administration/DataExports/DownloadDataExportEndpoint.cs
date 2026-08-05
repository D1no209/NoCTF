using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.DataExports;

namespace NoCTF.API.Endpoints.Administration.DataExports;

public sealed class DownloadDataExportEndpoint(
    AccessDataExport access,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/data-exports/{dataExportId}/download");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDownloadDataExport"));
        Summary(summary =>
        {
            summary.Summary = "Downloads an available data export artifact.";
            summary.Description =
                "Revalidates current scope access before streaming an available, unexpired export artifact.";
        });
    }

    public override async Task<
        Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(CancellationToken cancellationToken)
    {
        var result = await access.ExecuteAsync(new AccessDataExportQuery(
            Route<Guid>("dataExportId"),
            user.UserId,
            user.IsAdministrator), cancellationToken);
        if (result.Failure == AccessDataExportFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Failure == AccessDataExportFailure.Forbidden)
            return TypedResults.Forbid();
        if (result.Failure == AccessDataExportFailure.Expired)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "The data export has expired.");
        }
        if (result.Failure == AccessDataExportFailure.ObjectMissing)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "The data export artifact is no longer available.");
        }
        if (result.Failure is not null || result.Download is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The data export is not ready.");
        }
        return TypedResults.Stream(
            result.Download.Content,
            result.Download.ContentType,
            result.Download.FileName);
    }
}
