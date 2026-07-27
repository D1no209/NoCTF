using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class GetDeadLetterEndpoint(ManagePlatform platform)
    : EndpointWithoutRequest<Results<Ok<DeadLetterResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/platform/dead-letters/{messageId}");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformGetDeadLetter"));
        Summary(summary =>
        {
            summary.Summary = "Gets redacted dead-letter metadata.";
            summary.Description = "Returns delivery metadata without message bodies, secrets, or exception text.";
        });
    }

    public override async Task<Results<Ok<DeadLetterResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await platform.GetDeadLetterAsync(Route<Guid>("messageId"), ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(DeadLetterMapping.ToResponse(result));
    }
}
