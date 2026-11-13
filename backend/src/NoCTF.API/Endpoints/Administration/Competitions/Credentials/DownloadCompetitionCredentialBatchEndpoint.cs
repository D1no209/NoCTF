using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;
using NoCTF.Infrastructure.Competitions.Credentials;

namespace NoCTF.API.Endpoints.Administration.Competitions.Credentials;

public sealed class DownloadCompetitionCredentialBatchEndpoint(
    ReadCompetitionCredentialBatch read,
    CompetitionCredentialExportService export,
    IUserContext user,
    TimeProvider clock)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/credential-batches/{batchId}/download");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDownloadCompetitionCredentialBatch"));
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var value = await read.ExecuteAsync(new(
            Route<Guid>("competitionId"), Route<Guid>("batchId"), user.UserId, clock.GetUtcNow()), ct);
        if (value is null)
            return TypedResults.NotFound();
        var archive = export.CreateZip(value);
        return TypedResults.Stream(archive.Content, archive.ContentType, archive.FileName);
    }
}
