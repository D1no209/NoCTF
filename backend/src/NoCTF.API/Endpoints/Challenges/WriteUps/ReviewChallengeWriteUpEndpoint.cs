using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class ReviewChallengeWriteUpRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid WriteUpId { get; set; }
    public Guid VersionId { get; set; }
    public Guid ExpectedStamp { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<WriteUpReviewAction>))]
    [JsonRequired]
    public WriteUpReviewAction Action { get; set; }
    public string? Reason { get; set; }
}
public sealed class ReviewChallengeWriteUpValidator : Validator<ReviewChallengeWriteUpRequest>
{
    public ReviewChallengeWriteUpValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.CompetitionChallengeId).NotEmpty();
        RuleFor(x => x.WriteUpId).NotEmpty(); RuleFor(x => x.VersionId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
        RuleFor(x => x.Action).IsInEnum(); RuleFor(x => x.Reason).MaximumLength(4000);
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Action == WriteUpReviewAction.Reject);
    }
}
public sealed class ReviewChallengeWriteUpEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<ReviewChallengeWriteUpRequest, Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>, UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/{writeUpId}/review"); AuthSchemes("Bearer");
        Description(x => x.WithName("ReviewChallengeWriteUp"));
        Summary(x => x.Summary = "Publishes, rejects or withdraws a specific reviewed version with role checks.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>, UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
        ExecuteAsync(ReviewChallengeWriteUpRequest request, CancellationToken ct) =>
        ChallengeWriteUpProtocol.Mutation(await writeUps.ReviewAsync(new(request.CompetitionId,
            request.CompetitionChallengeId, request.WriteUpId, request.VersionId, user.UserId, request.ExpectedStamp,
            request.Action, request.Reason, clock.GetUtcNow()), ct));
}
