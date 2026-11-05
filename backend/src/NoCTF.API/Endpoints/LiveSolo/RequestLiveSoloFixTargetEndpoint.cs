using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class RequestLiveSoloFixTargetRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
}
public sealed class RequestLiveSoloFixTargetValidator : Validator<RequestLiveSoloFixTargetRequest>
{
    public RequestLiveSoloFixTargetValidator()
    { RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.MatchId).NotEmpty(); RuleFor(value => value.RoundId).NotEmpty(); RuleFor(value => value.QuestionId).NotEmpty(); }
}
public sealed class RequestLiveSoloFixTargetEndpoint(ManageLiveSoloFixTargets targets, IUserContext user, TimeProvider clock)
    : Endpoint<RequestLiveSoloFixTargetRequest, Results<Accepted<LiveSoloFixTargetResponse>, NotFound, Conflict<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/fix-target");
        AuthSchemes("Bearer"); Description(builder => builder.WithName("RequestLiveSoloFixTarget"));
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.RuntimeCommand), new EnableRateLimitingAttribute("submission"),
            new HumanVerificationMetadata(HumanVerificationAction.Evaluation)));
    }
    public override async Task<Results<Accepted<LiveSoloFixTargetResponse>, NotFound, Conflict<LiveSoloFailureResponse>, ProblemHttpResult>>
        ExecuteAsync(RequestLiveSoloFixTargetRequest request, CancellationToken ct)
    {
        var target = await targets.RequestAsync(new(request.CompetitionId, request.MatchId, request.RoundId, request.QuestionId, user.UserId, clock.GetUtcNow()), ct);
        if (target.Failure is null) return TypedResults.Accepted((string?)null, new LiveSoloFixTargetResponse(target.RuntimeInstanceId, target.State, target.GameplayFactId,
            $"/api/v1/competitions/{request.CompetitionId}/live-solo/matches/{request.MatchId}/rounds/{request.RoundId}/questions/{request.QuestionId}/fix-target"));
        return target.Failure switch
        {
            LiveSoloFailure.NotFound or LiveSoloFailure.Forbidden => TypedResults.NotFound(),
            LiveSoloFailure.DependencyUnavailable => LiveSoloProtocol.Unavailable(target.Failure.Value),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(target.Failure.Value))
        };
    }
}
