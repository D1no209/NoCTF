using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SubmitLiveSoloFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
    public string Flag { get; set; } = string.Empty;
}
public sealed class SubmitLiveSoloFlagValidator : Validator<SubmitLiveSoloFlagRequest>
{
    public SubmitLiveSoloFlagValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.RoundId).NotEmpty(); RuleFor(x => x.QuestionId).NotEmpty(); RuleFor(x => x.Flag).NotEmpty().MaximumLength(4096);
    }
}
public sealed record LiveSoloFlagAcceptedResponse(Guid GameplayFactId, long AdmissionSequence, DateTimeOffset AcceptedAt, string StatusUrl);
public sealed class SubmitLiveSoloFlagEndpoint(ManageLiveSoloMatches matches, IUserContext user, TimeProvider clock)
    : Endpoint<SubmitLiveSoloFlagRequest, Results<Accepted<LiveSoloFlagAcceptedResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/flag-submissions"); AuthSchemes("Bearer");
        MaxRequestBodySize(16 * 1024);
        Options(x => x.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.FlagSubmission), new EnableRateLimitingAttribute("submission"),
            new HumanVerificationMetadata(HumanVerificationAction.Evaluation)));
        Description(x => x.WithName("SubmitLiveSoloFlag"));
        Summary(x => x.Summary = "Atomically admits one Flag to the Round-wide server order; requires a UUID Idempotency-Key.");
    }
    public override async Task<Results<Accepted<LiveSoloFlagAcceptedResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
        ExecuteAsync(SubmitLiveSoloFlagRequest req, CancellationToken ct)
    {
        var result = await matches.SubmitAsync(new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionId, user.UserId, req.Flag, clock.GetUtcNow()), ct);
        if (result.Failure is null && result.GameplayFactId is Guid id)
            return TypedResults.Accepted((string?)null, new LiveSoloFlagAcceptedResponse(id, result.Sequence!.Value, result.AcceptedAt!.Value,
                $"/api/v1/competitions/{req.CompetitionId}/gameplay-facts/{id}"));
        return result.Failure switch
        {
            LiveSoloFailure.NotFound => TypedResults.NotFound(), LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            LiveSoloFailure.DependencyUnavailable => LiveSoloProtocol.Unavailable(result.Failure.Value),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict))
        };
    }
}
