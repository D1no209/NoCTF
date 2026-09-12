using System.Reflection;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PlatformContributorResponse(string Id, string AvatarUrl);

public sealed record PlatformInformationResponse(
    string Version,
    IReadOnlyList<PlatformContributorResponse> Contributors);

public sealed class GetPlatformInformationEndpoint
    : EndpointWithoutRequest<Ok<PlatformInformationResponse>>
{
    private static readonly PlatformContributorResponse[] Contributors =
    [
        new("Fa1lSnow", "https://avatars.githubusercontent.com/u/76813774?v=4"),
        new("kengwang", "https://avatars.githubusercontent.com/u/30862240?v=4"),
        new("lQ-A-Ql", "https://avatars.githubusercontent.com/u/83216887?v=4"),
        new("evnrowa", "https://avatars.githubusercontent.com/u/123802298?v=4")
    ];

    public override void Configure()
    {
        Get("/admin/platform/information");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformGetInformation"));
        Summary(summary =>
        {
            summary.Summary = "Returns the running platform version and contributors.";
            summary.Description = "Reports build metadata and the repository contributor identity snapshot.";
        });
    }

    public override Task<Ok<PlatformInformationResponse>> ExecuteAsync(CancellationToken ct)
    {
        var assembly = typeof(GetPlatformInformationEndpoint).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        return Task.FromResult(TypedResults.Ok(new PlatformInformationResponse(
            version,
            Contributors)));
    }
}
