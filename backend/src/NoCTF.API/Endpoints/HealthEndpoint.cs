using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace NoCTF.API.Endpoints;

public sealed record HealthResponse(string Status);

public sealed class HealthEndpoint : EndpointWithoutRequest<Ok<HealthResponse>>
{
    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
    }

    public override Task<Ok<HealthResponse>> ExecuteAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TypedResults.Ok(new HealthResponse("ok")));
}
