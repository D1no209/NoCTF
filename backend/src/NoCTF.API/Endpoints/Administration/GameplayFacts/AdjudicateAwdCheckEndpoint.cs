using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class AdjudicateAwdCheckRequest
{
    public Guid? ExpectedAttemptId { get; set; }
    public DateTimeOffset? ExpectedReceivedAt { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdCheckAdjudicationAction>))]
    public AwdCheckAdjudicationAction? Action { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AdjudicateAwdCheckValidator : Validator<AdjudicateAwdCheckRequest>
{
    public AdjudicateAwdCheckValidator()
    {
        RuleFor(request => request.Action).NotNull().IsInEnum();
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(512);
    }
}

public sealed class AdjudicateAwdCheckEndpoint(AdjudicateAwdCheck adjudicate,
    ICompetitionModerationAuthorizer authorizer, IUserContext user, TimeProvider clock)
    : Endpoint<AdjudicateAwdCheckRequest,
        Results<Ok<AwdCheckEvidenceResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/awd/checks/{gameplayFactId}/adjudication");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminAdjudicateAwdCheck"));
        Summary(summary => summary.Summary = "Confirms saved AWD evidence or explicitly exempts a pending check with an audited reason.");
    }

    public override async Task<Results<Ok<AwdCheckEvidenceResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(AdjudicateAwdCheckRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await adjudicate.ExecuteAsync(new(competitionId, Route<Guid>("gameplayFactId"),
            request.ExpectedAttemptId, request.ExpectedReceivedAt, user.UserId, request.Action!.Value, request.Reason, clock.GetUtcNow()), ct);
        return result.State switch
        {
            AwdCheckAdjudicationState.Applied => TypedResults.Ok(AwdCheckEvidenceResponse.From(result.View!)),
            AwdCheckAdjudicationState.NotFound => TypedResults.NotFound(),
            _ => ApiProblems.Problem(statusCode: result.State == AwdCheckAdjudicationState.Conflict
                    ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity,
                detail: ApiMessages.For(result.State),
                extensions: new Dictionary<string, object?> { ["code"] = result.State.ToString() })
        };
    }
}
