using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;

namespace NoCTF.API.Endpoints.Administration.Competitions.Credentials;

public sealed record ListCompetitionCredentialBatchesResponse(
    IReadOnlyList<CompetitionCredentialBatchSummary> Items);

public sealed class ListCompetitionCredentialBatchesEndpoint(
    ListCompetitionCredentialBatches list,
    IUserContext user)
    : EndpointWithoutRequest<Ok<ListCompetitionCredentialBatchesResponse>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/credential-batches");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionCredentialBatches"));
    }

    public override async Task<Ok<ListCompetitionCredentialBatchesResponse>> ExecuteAsync(CancellationToken ct) =>
        TypedResults.Ok(new ListCompetitionCredentialBatchesResponse(
            await list.ExecuteAsync(Route<Guid>("competitionId"), user.UserId, ct)));
}
