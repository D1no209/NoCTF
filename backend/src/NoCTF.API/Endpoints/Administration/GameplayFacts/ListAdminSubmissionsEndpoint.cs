using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Gameplay;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class ListAdminGameplayFactsRequest : PaginationRequest
{
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public Guid? VictimTeamId { get; set; }
    [QueryParam] public Guid? ActorUserId { get; set; }
    [QueryParam] public GameplayFactKindProtocol? GameplayFactKind { get; set; }
    [QueryParam] public GameplayFactStateProtocol? State { get; set; }
    [QueryParam] public GameplayFactResultProtocol? GameplayFactResult { get; set; }
    [QueryParam] public GameplayFactFailureCodeProtocol? FailureCode { get; set; }
    [QueryParam] public DateTimeOffset? OccurredFrom { get; set; }
    [QueryParam] public DateTimeOffset? OccurredTo { get; set; }
    [QueryParam] public string? Value { get; set; }
    [QueryParam] public GameplayFactReferenceKind? ReferenceKind { get; set; }
    [QueryParam] public Guid? ReferenceId { get; set; }
}

public sealed class ListAdminGameplayFactsValidator : Validator<ListAdminGameplayFactsRequest>
{
    public ListAdminGameplayFactsValidator() =>
        PaginationRules.Add(this);
}

public sealed class ListAdminGameplayFactsEndpoint(
    ListGameplayFacts list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListAdminGameplayFactsRequest,
        Results<Ok<GameplayFactListResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/gameplay-facts");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListGameplayFacts"));
        Summary(summary =>
        {
            summary.Summary = "Lists filtered competition gameplay facts.";
            summary.Description = "Returns offset-paged protected gameplay facts to authorized competition observers.";
        });
    }

    public override async Task<Results<Ok<GameplayFactListResponse>, ForbidHttpResult>> ExecuteAsync(
        ListAdminGameplayFactsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var filter = new GameplayFactListFilter(
            competitionId, request.CompetitionChallengeId, request.TeamId, request.VictimTeamId,
            request.ActorUserId,
            request.GameplayFactKind is null ? null : GameplayFactMapper.ToDomain(request.GameplayFactKind.Value),
            request.State is null ? null : GameplayFactMapper.ToDomain(request.State.Value),
            request.GameplayFactResult is null ? null : GameplayFactMapper.ToDomain(request.GameplayFactResult.Value),
            request.FailureCode is null ? null : GameplayFactMapper.ToDomain(request.FailureCode.Value),
            request.OccurredFrom, request.OccurredTo,
            request.Value, request.ReferenceKind, request.ReferenceId);
        var page = await list.AdminPageAsync(filter, request.Offset, request.Limit, request.Desc, ct);
        return TypedResults.Ok(new GameplayFactListResponse(
            page.Items.Select(GameplayFactListMapping.ToAdminResponse).ToArray(),
            page.Total));
    }
}
