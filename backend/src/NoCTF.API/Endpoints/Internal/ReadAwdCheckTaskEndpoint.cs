using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Competitions.Awd;

namespace NoCTF.API.Endpoints.Internal;

public sealed record AwdCheckTaskFlagResponse(Guid FlagId, Guid StoreId, string StoreName, string? TargetServiceName,
    string Flag, bool Historical, bool RequiresWrite, string? PrivateReadContext, string? PublicLocator)
{
    public override string ToString() => $"AwdCheckTaskFlagResponse {{ FlagId = {FlagId}, Material = [REDACTED] }}";
}

public sealed record AwdCheckTaskResponse(Guid GameplayFactId, Guid AttemptId, DateTimeOffset Deadline,
    IReadOnlyList<AwdCheckTaskFlagResponse> Flags);

public sealed class ReadAwdCheckTaskEndpoint(IAwdCheckerTaskReader reader, TimeProvider clock)
    : EndpointWithoutRequest<Results<Ok<AwdCheckTaskResponse>, NotFound, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/api/internal/v1/awd/check-task");
        AuthSchemes("Internal");
        Policies("AwdCheckTask");
        RoutePrefixOverride(string.Empty);
        Summary(summary => summary.Summary = "Reads only the flag material assigned to the current AWD checker attempt.");
    }

    public override async Task<Results<Ok<AwdCheckTaskResponse>, NotFound, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeId)
            || !Guid.TryParse(User.FindFirstValue("gameplay_fact_id"), out var factId)
            || !Guid.TryParse(User.FindFirstValue("attempt_id"), out var attemptId)
            || User.FindFirstValue("resource") != $"runtime:{runtimeId:D}")
            return TypedResults.Unauthorized();
        var task = await reader.ReadAsync(runtimeId, factId, attemptId, clock.GetUtcNow(), ct);
        return task is null ? TypedResults.NotFound()
            : TypedResults.Ok(new AwdCheckTaskResponse(task.GameplayFactId, task.AttemptId, task.Deadline,
                task.Flags.Select(flag => new AwdCheckTaskFlagResponse(flag.FlagId, flag.StoreId, flag.StoreName,
                    flag.TargetServiceName, flag.Flag, flag.Historical, flag.RequiresWrite, flag.PrivateReadContext, flag.PublicLocator)).ToArray()));
    }
}
