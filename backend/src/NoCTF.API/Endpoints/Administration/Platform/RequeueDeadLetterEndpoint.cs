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
        Summary(summary => summary.Summary = "Marks one Wolverine dead letter for durable replay.");
    }

    public override async Task<Results<Accepted, NotFound>> ExecuteAsync(CancellationToken ct) =>
        await platform.RequeueDeadLetterAsync(Route<Guid>("messageId"), ct)
            ? TypedResults.Accepted((string?)null)
            : TypedResults.NotFound();
}
