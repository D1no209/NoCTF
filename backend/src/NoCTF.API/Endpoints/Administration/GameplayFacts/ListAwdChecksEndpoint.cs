using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class ListAwdChecksRequest : PaginationRequest
{
    [QueryParam] public AwdSettlementState? Settlement { get; set; } = AwdSettlementState.PendingAdjudication;
    [QueryParam] public bool AllSettlements { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public int? RoundNumber { get; set; }
}

public sealed class ListAwdChecksValidator : Validator<ListAwdChecksRequest>
{
    public ListAwdChecksValidator()
    {
        PaginationRules.Add(this);
        RuleFor(request => request.Settlement).IsInEnum();
        RuleFor(request => request.RoundNumber).GreaterThan(0);
    }
}

public sealed record AwdCheckListItemResponse(Guid GameplayFactId, Guid CompetitionChallengeId, Guid TeamId,
    Guid RoundId, int RoundNumber, DateTimeOffset ScheduledAt,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdHealth>))] AwdHealth? Health,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdCheckExecution>))] AwdCheckExecution Execution,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdSettlementState>))] AwdSettlementState Settlement,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdCheckReason>))] AwdCheckReason Reason,
    bool FinalScheduledCheck, DateTimeOffset? ReceivedAt);

public sealed class ListAwdChecksEndpoint(IAwdCheckAdjudicationStore store,
    ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<ListAwdChecksRequest, Results<Ok<ArrayResult<AwdCheckListItemResponse>>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/awd/checks");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListAwdChecks"));
        Summary(summary => summary.Summary = "Lists AWD checks, defaulting to pending adjudication; evidence is loaded separately.");
    }

    public override async Task<Results<Ok<ArrayResult<AwdCheckListItemResponse>>, ForbidHttpResult>>
        ExecuteAsync(ListAwdChecksRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        HttpContext.Response.Headers.CacheControl = "private,no-store";
        var view = await store.ListAsync(new(competitionId, request.Offset, request.Limit,
            request.AllSettlements ? null : request.Settlement, request.TeamId,
            request.CompetitionChallengeId, request.RoundNumber), ct);
        return TypedResults.Ok(new ArrayResult<AwdCheckListItemResponse>(view.Items.Select(item =>
            new AwdCheckListItemResponse(item.GameplayFactId, item.CompetitionChallengeId, item.TeamId,
                item.RoundId, item.RoundNumber, item.ScheduledAt, item.Health, item.Execution,
                item.Settlement, item.Reason, item.FinalScheduledCheck, item.ReceivedAt)).ToArray(), view.Total));
    }
}
