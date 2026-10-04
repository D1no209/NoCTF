using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.Runtime;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Pagination;
using NoCTF.API.Serialization;
using NoCTF.Application.Runtime.Instances;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ListPlatformRuntimesRequest : PaginationRequest
{
    [QueryParam] public string? Search { get; set; }
    [QueryParam] public PlatformRuntimeScopeProtocol? Scope { get; set; }
    [QueryParam] public RuntimeStateProtocol? State { get; set; }
    [QueryParam] public RuntimeKindProtocol? RuntimeKind { get; set; }
}

public sealed class ListPlatformRuntimesValidator : Validator<ListPlatformRuntimesRequest>
{
    public ListPlatformRuntimesValidator()
    {
        PaginationRules.Add(this);
        RuleFor(request => request.Search).MaximumLength(200);
        RuleFor(request => request.Scope).IsInEnum();
        RuleFor(request => request.State).Must(value => value is null
            or RuntimeStateProtocol.Queued or RuntimeStateProtocol.Provisioning
            or RuntimeStateProtocol.Running or RuntimeStateProtocol.Stopping)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.ListPlatformRuntimesValidationStateQueuedProvisioningRunning)).WithErrorCode(ApiMessages.Key(ApiMessageId.ListPlatformRuntimesValidationStateQueuedProvisioningRunning));
        RuleFor(request => request.RuntimeKind).Must(value => value is null
            or RuntimeKindProtocol.Container)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.ListPlatformRuntimesValidationRuntimekindContainer)).WithErrorCode(ApiMessages.Key(ApiMessageId.ListPlatformRuntimesValidationRuntimekindContainer));
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

public sealed class PlatformRuntimeListResponse : ArrayResult<PlatformRuntimeResponse>
{
    public PlatformRuntimeListResponse() { }

    public PlatformRuntimeListResponse(PlatformRuntimeResponse[] items, int total)
        : base(items, total) { }
}

public sealed class ListPlatformRuntimesEndpoint(
    ManageAdminRuntimes runtimes,
    TimeProvider timeProvider)
    : Endpoint<ListPlatformRuntimesRequest, Ok<PlatformRuntimeListResponse>>
{
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
                "Returns offset-paged Container runtimes in active lifecycle states to platform administrators. "
                + "Supports scope, state, kind and case-insensitive title/team search, including Fix target team attribution.";
        });
    }

    public override async Task<Ok<PlatformRuntimeListResponse>> ExecuteAsync(
        ListPlatformRuntimesRequest request,
        CancellationToken ct)
    {
        var search = request.Search?.Trim();
        var runtimeFilter = new PlatformRuntimeFilter(
                search,
                request.Scope is null ? null : request.Scope == PlatformRuntimeScopeProtocol.ChallengeTest
                    ? PlatformRuntimeScope.ChallengeTest : PlatformRuntimeScope.Competition,
                request.State is null ? null : RuntimeProtocolMapper.ToDomain(request.State.Value),
                request.RuntimeKind is null ? null : RuntimeProtocolMapper.ToDomain(request.RuntimeKind.Value));
        var page = await runtimes.ListActiveContainersPageAsync(
            runtimeFilter,
            request.Offset,
            request.Limit,
            request.Desc,
            ct);
        var current = timeProvider.GetUtcNow();
        return TypedResults.Ok(new PlatformRuntimeListResponse(
            page.Items.Select(item => new PlatformRuntimeResponse(
                    AdminRuntimeMapping.ToResponse(
                        item.Runtime,
                        HttpContext.Request,
                        current,
                        includeCapacity: true),
                    item.Scope == PlatformRuntimeScope.ChallengeTest
                        ? PlatformRuntimeScopeProtocol.ChallengeTest
                        : PlatformRuntimeScopeProtocol.Competition,
                    item.CompetitionTitle,
                    item.ChallengeTitle))
                .ToArray(),
            page.Total));
    }
}
