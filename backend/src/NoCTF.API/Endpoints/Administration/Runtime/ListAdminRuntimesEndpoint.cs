using System.Globalization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Runtime;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class ListAdminRuntimesRequest
{
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public RuntimeKindProtocol? RuntimeKind { get; set; }
    [QueryParam] public RuntimeProviderProtocol? Provider { get; set; }
    [QueryParam] public string? RunnerPool { get; set; }
    [QueryParam] public string? RunnerId { get; set; }
    [QueryParam] public RuntimeStateProtocol? State { get; set; }
    [QueryParam] public DateTimeOffset? ExpiresBefore { get; set; }
    [QueryParam] public int? HostPort { get; set; }
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class ListAdminRuntimesValidator : Validator<ListAdminRuntimesRequest>
{
    public ListAdminRuntimesValidator()
    {
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
        RuleFor(request => request.HostPort)
            .InclusiveBetween(1, 65535)
            .When(request => request.HostPort.HasValue);
    }
}

public sealed record AdminRuntimeResponse(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    Guid? SourceTeamId,
    string? SourceTeamName,
    RuntimePurposeProtocol Purpose,
    int Generation,
    RuntimeKindProtocol RuntimeKind,
    RuntimeProviderProtocol Provider,
    string RunnerPool,
    string? RunnerId,
    RuntimeStateProtocol State,
    RuntimeFailureCodeProtocol? FailureCode,
    IReadOnlyList<string> Urls,
    string? ProviderReceiptJson,
    string? ControlCheckUrl,
    IReadOnlyList<RuntimePublishedPortView> PublishedPorts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RunningAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StoppedAt,
    DateTimeOffset? ForceTerminationAvailableAt,
    bool CanForceTerminate);

public sealed record AdminRuntimeListResponse(
    IReadOnlyList<AdminRuntimeResponse> Items,
    string? NextCursor);

internal static class AdminRuntimeMapping
{
    public static AdminRuntimeResponse ToResponse(RuntimeInstanceView view)
    {
        var availableAt = RuntimeForceTerminationPolicy.AvailableAt(view);
        return new(
            view.Id, view.CompetitionId, view.CompetitionChallengeId, view.TeamId,
            view.SourceTeamId, view.SourceTeamName,
            RuntimeProtocolMapper.ToProtocol(view.Purpose),
            view.Generation,
            RuntimeProtocolMapper.ToProtocol(view.RuntimeKind),
            RuntimeProtocolMapper.ToProtocol(view.Provider),
            view.RunnerPool,
            view.RunnerId,
            RuntimeProtocolMapper.ToProtocol(view.State),
            view.FailureCode is null ? null : RuntimeProtocolMapper.ToProtocol(view.FailureCode.Value),
            view.Urls, view.ProviderReceiptJson, view.ControlCheckUrl,
            view.PublishedPorts ?? [], view.CreatedAt,
            view.RunningAt, view.ExpiresAt, view.StoppedAt,
            availableAt,
            availableAt is { } value && value <= DateTimeOffset.UtcNow);
    }
}

public sealed class ListAdminRuntimesEndpoint(
    ManageAdminRuntimes runtimes,
    SignedKeysetCursor cursors,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListAdminRuntimesRequest,
        Results<Ok<AdminRuntimeListResponse>, ForbidHttpResult, ProblemHttpResult>>
{
    private const string CursorEndpoint = "runtimes.admin.list";

    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/runtimes");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListRuntimes"));
        Summary(summary =>
        {
            summary.Summary = "Lists filtered runtime instances.";
            summary.Description = "Returns keyset-paged runtime state and placement metadata to authorized observers.";
        });
    }

    public override async Task<Results<Ok<AdminRuntimeListResponse>, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        ListAdminRuntimesRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var filterKey = string.Join(
            '|', request.CompetitionChallengeId, request.TeamId, request.RuntimeKind,
            request.Provider, request.RunnerPool, request.RunnerId, request.State,
            request.ExpiresBefore?.ToString("O", CultureInfo.InvariantCulture),
            request.HostPort);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filterKey, out var position))
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid cursor.");
        var items = await runtimes.ListAsync(new(
                competitionId, request.CompetitionChallengeId, request.TeamId,
                request.RuntimeKind is null ? null : RuntimeProtocolMapper.ToDomain(request.RuntimeKind.Value),
                request.Provider is null ? null : RuntimeProtocolMapper.ToDomain(request.Provider.Value),
                request.RunnerPool,
                request.RunnerId,
                request.State is null ? null : RuntimeProtocolMapper.ToDomain(request.State.Value),
                request.ExpiresBefore,
                request.HostPort),
            position?.CreatedAt, position?.Id, request.Limit, ct);
        var next = items.Count == request.Limit
            ? cursors.Encode(CursorEndpoint, filterKey, new(items[^1].CreatedAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new AdminRuntimeListResponse(
            items.Select(AdminRuntimeMapping.ToResponse).ToArray(), next));
    }
}
