using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class SubmitChallengeWriteUpRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public bool Official { get; set; }
    public Guid ExpectedStamp { get; set; }
}
public sealed class SubmitChallengeWriteUpValidator : Validator<SubmitChallengeWriteUpRequest>
{
    public SubmitChallengeWriteUpValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.CompetitionChallengeId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
    }
}
public sealed class SubmitChallengeWriteUpEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<SubmitChallengeWriteUpRequest, Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>, UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/submit"); AuthSchemes("Bearer");
        Description(x => x.WithName("SubmitChallengeWriteUp"));
        Summary(x => x.Summary = "Freezes the current draft and submits a version for review.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>, UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
        ExecuteAsync(SubmitChallengeWriteUpRequest request, CancellationToken ct) =>
        ChallengeWriteUpProtocol.Mutation(await writeUps.SubmitAsync(new(request.CompetitionId,
            request.CompetitionChallengeId, user.UserId, request.Official, request.ExpectedStamp, clock.GetUtcNow()), ct));
}
