using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;
using NoCTF.Infrastructure.Competitions.Credentials;

namespace NoCTF.API.Endpoints.Administration.Competitions.Credentials;

public sealed class DownloadCompetitionCredentialBatchPdfEndpoint(
    ReadCompetitionCredentialBatch read,
    CompetitionCredentialExportService export,
    IUserContext user,
    TimeProvider clock)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/credential-batches/{batchId}/print.pdf");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDownloadCompetitionCredentialBatchPdf"));
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var value = await read.ExecuteAsync(new(
            Route<Guid>("competitionId"), Route<Guid>("batchId"), user.UserId, clock.GetUtcNow()), ct);
        if (value is null)
            return TypedResults.NotFound();
        HttpContext.Response.Headers.CacheControl = "no-store";
        HttpContext.Response.Headers.Pragma = "no-cache";
        var pdf = export.CreatePdf(value);
        return TypedResults.Stream(pdf, "application/pdf", $"noctf-credentials-{value.BatchId:N}.pdf");
    }
}
