using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.Runtime;
using NoCTF.API.Pagination;
using NoCTF.API.Serialization;
using NoCTF.Application.Runtime.Instances;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ListPlatformRuntimesRequest
{
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class ListPlatformRuntimesValidator : Validator<ListPlatformRuntimesRequest>
{
    public ListPlatformRuntimesValidator()
    {
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PlatformRuntimeScopeProtocol>))]
public enum PlatformRuntimeScopeProtocol
{
    Competition,
    ChallengeTest
}

public sealed record PlatformRuntimeResponse(
    AdminRuntimeResponse Runtime,
    PlatformRuntimeScopeProtocol Scope,
    string? CompetitionTitle,
    string ChallengeTitle);

public sealed record PlatformRuntimeListResponse(
    IReadOnlyList<PlatformRuntimeResponse> Items,
    string? NextCursor);

public sealed class ListPlatformRuntimesEndpoint(
    ManageAdminRuntimes runtimes,
    SignedKeysetCursor cursors,
    TimeProvider timeProvider)
    : Endpoint<ListPlatformRuntimesRequest,
        Results<Ok<PlatformRuntimeListResponse>, ProblemHttpResult>>
{
    private const string CursorEndpoint = "runtimes.platform.active.list";
    private const string FilterKey = "active-containers";

    public override void Configure()
    {
        Get("/admin/platform/runtimes");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformListActiveRuntimes"));
        Summary(summary =>
        {
            summary.Summary = "Lists active runtime containers across the platform.";
            summary.Description =
                "Returns keyset-paged Container and Compose runtimes in active lifecycle states to platform administrators.";
        });
    }

    public override async Task<Results<Ok<PlatformRuntimeListResponse>, ProblemHttpResult>> ExecuteAsync(
        ListPlatformRuntimesRequest request,
        CancellationToken ct)
    {
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, FilterKey, out var position))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        }

        var items = await runtimes.ListActiveContainersAsync(
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            ct);
        var next = items.Count == request.Limit
            ? cursors.Encode(
                CursorEndpoint,
                FilterKey,
                new(items[^1].Runtime.CreatedAt, items[^1].Runtime.Id))
            : null;
        var now = timeProvider.GetUtcNow();
        return TypedResults.Ok(new PlatformRuntimeListResponse(
            items.Select(item => new PlatformRuntimeResponse(
                    AdminRuntimeMapping.ToResponse(item.Runtime, now),
                    item.Scope == PlatformRuntimeScope.ChallengeTest
                        ? PlatformRuntimeScopeProtocol.ChallengeTest
                        : PlatformRuntimeScopeProtocol.Competition,
                    item.CompetitionTitle,
                    item.ChallengeTitle))
                .ToArray(),
            next));
    }
}
