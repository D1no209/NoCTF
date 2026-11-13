using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;

namespace NoCTF.API.Endpoints.Administration.Competitions.Credentials;

public sealed record GetCompetitionCredentialBatchResponse(
    Guid BatchId,
    Guid CompetitionId,
    DateTimeOffset CreatedAt,
    string SourceFileName,
    int PasswordVersion,
    IReadOnlyList<CompetitionCredentialDownloadRow> Rows);

public sealed class GetCompetitionCredentialBatchEndpoint(
    ReadCompetitionCredentialBatch read,
    IUserContext user,
    TimeProvider clock)
    : EndpointWithoutRequest<Results<Ok<GetCompetitionCredentialBatchResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/credential-batches/{batchId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionCredentialBatch"));
    }

    public override async Task<Results<Ok<GetCompetitionCredentialBatchResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var value = await read.ExecuteAsync(new(
            Route<Guid>("competitionId"),
            Route<Guid>("batchId"),
            user.UserId,
            clock.GetUtcNow()), ct);
        return value is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new GetCompetitionCredentialBatchResponse(
                value.BatchId, value.CompetitionId, value.CreatedAt, value.SourceFileName,
                value.PasswordVersion, value.Rows));
    }
}
