using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class ListRuntimeTrafficCapturesRequest : PaginationRequest
{
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public Guid? RuntimeInstanceId { get; set; }
    [QueryParam] public bool? Truncated { get; set; }
}

public sealed class ListRuntimeTrafficCapturesValidator
    : Validator<ListRuntimeTrafficCapturesRequest>
{
    public ListRuntimeTrafficCapturesValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed record RuntimeTrafficCaptureResponse(
    Guid RuntimeInstanceId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    RuntimeStateProtocol RuntimeState,
    int SegmentCount,
    long ByteLength,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    bool Truncated,
    string? TeamName,
    string? ChallengeTitle);

public sealed record RuntimeTrafficCaptureListResponse(
    IReadOnlyList<RuntimeTrafficCaptureResponse> Items,
    int Total);

public sealed class ListRuntimeTrafficCapturesEndpoint(
    ManageRuntimeTrafficCaptures captures,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListRuntimeTrafficCapturesRequest,
        Results<Ok<RuntimeTrafficCaptureListResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/traffic-captures");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListRuntimeTrafficCaptures"));
        Summary(summary => { summary.Summary = "Lists Runtime traffic captures."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<RuntimeTrafficCaptureListResponse>,
        ForbidHttpResult>> ExecuteAsync(
        ListRuntimeTrafficCapturesRequest request,
        CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(
                user.UserId,
                competitionId,
                cancellationToken))
            return TypedResults.Forbid();
        var page = await captures.ListAsync(new(
            competitionId,
            request.CompetitionChallengeId,
            request.TeamId,
            request.RuntimeInstanceId,
            request.Truncated,
            request.Offset,
            request.Limit), cancellationToken);
        return TypedResults.Ok(new RuntimeTrafficCaptureListResponse(
            page.Items.Select(item => new RuntimeTrafficCaptureResponse(
                item.RuntimeInstanceId,
                item.CompetitionChallengeId,
                item.TeamId,
                RuntimeProtocolMapper.ToProtocol(item.RuntimeState),
                item.SegmentCount,
                item.ByteLength,
                item.StartedAt,
                item.UpdatedAt,
                item.Truncated,
                item.TeamName,
                item.ChallengeTitle)).ToArray(),
            page.Total));
    }
}
