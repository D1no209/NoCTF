using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<HealthStatus>))]
public enum HealthStatus
{
    Ok
}

public sealed record HealthResponse(HealthStatus Status);

public sealed class HealthEndpoint : EndpointWithoutRequest<Ok<HealthResponse>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Reports API process liveness without accessing business storage.";
            summary.Description = summary.Summary;
        });

        Get("/health");
        RoutePrefixOverride(string.Empty);
        AllowAnonymous();
    }

    public override Task<Ok<HealthResponse>> ExecuteAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TypedResults.Ok(new HealthResponse(HealthStatus.Ok)));
}
