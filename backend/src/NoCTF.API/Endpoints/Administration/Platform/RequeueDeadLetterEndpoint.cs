using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class RequeueDeadLetterEndpoint(ManagePlatform platform)
    : EndpointWithoutRequest<Results<Accepted, NotFound>>
{
    public override void Configure()
    {
        Post("/admin/platform/dead-letters/{messageId}/requeue");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformRequeueDeadLetter"));
        Summary(summary =>
        {
            summary.Summary = "Queues a Wolverine dead letter for durable replay.";
            summary.Description = "Creates a new delivery attempt while preserving the original failed record.";
        });
    }

    public override async Task<Results<Accepted, NotFound>> ExecuteAsync(CancellationToken ct) =>
        await platform.RequeueDeadLetterAsync(Route<Guid>("messageId"), ct)
            ? TypedResults.Accepted((string?)null)
            : TypedResults.NotFound();
}
