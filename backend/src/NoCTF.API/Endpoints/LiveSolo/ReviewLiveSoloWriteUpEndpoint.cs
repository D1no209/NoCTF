using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges.WriteUps;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ReviewLiveSoloWriteUpRequest
{
    public Guid CompetitionId {get;set;}
    public Guid MatchId {get;set;}
    public Guid RoundId {get;set;}
    public Guid QuestionId {get;set;}
    public Guid WriteUpId {get;set;}
    public Guid VersionId {get;set;}
    public Guid ExpectedStamp {get;set;}
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<WriteUpReviewAction>))] public required WriteUpReviewAction Action {get;set;}
    public string? Reason {get;set;}
}
public sealed class ReviewLiveSoloWriteUpValidator:Validator<ReviewLiveSoloWriteUpRequest>
{
    public ReviewLiveSoloWriteUpValidator()
    {
        RuleFor(x=>x.CompetitionId).NotEmpty();RuleFor(x=>x.MatchId).NotEmpty();RuleFor(x=>x.RoundId).NotEmpty();RuleFor(x=>x.QuestionId).NotEmpty();
        RuleFor(x=>x.WriteUpId).NotEmpty();RuleFor(x=>x.VersionId).NotEmpty();RuleFor(x=>x.ExpectedStamp).NotEmpty();RuleFor(x=>x.Action).IsInEnum();
        RuleFor(x=>x.Reason).MaximumLength(4000);RuleFor(x=>x.Reason).NotEmpty().When(x=>x.Action==WriteUpReviewAction.Reject);
    }
}
public sealed class ReviewLiveSoloWriteUpEndpoint(ManageLiveSoloWriteUps writeUps,IUserContext user,TimeProvider clock)
    :Endpoint<ReviewLiveSoloWriteUpRequest,Results<Ok<ChallengeWriteUpResponse>,NotFound,ForbidHttpResult,Conflict<ChallengeWriteUpFailureResponse>,UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/writeups/{writeUpId}/review");AuthSchemes("Bearer");
        Description(x=>x.WithName("ReviewLiveSoloWriteUp"));Summary(x=>x.Summary="Reviews a post-event scoped version with the existing manager/judge permission matrix.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpResponse>,NotFound,ForbidHttpResult,Conflict<ChallengeWriteUpFailureResponse>,UnprocessableEntity<ChallengeWriteUpFailureResponse>>> ExecuteAsync(ReviewLiveSoloWriteUpRequest req,CancellationToken ct)
        =>ChallengeWriteUpProtocol.Mutation(await writeUps.ReviewAsync(new(req.CompetitionId,req.MatchId,req.RoundId,req.QuestionId,user.UserId,clock.GetUtcNow()),req.WriteUpId,req.VersionId,req.ExpectedStamp,req.Action,req.Reason,ct));
}
