using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges.WriteUps;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SubmitLiveSoloWriteUpRequest
{
    public Guid CompetitionId {get;set;}
    public Guid MatchId {get;set;}
    public Guid RoundId {get;set;}
    public Guid QuestionId {get;set;}
    public bool Official {get;set;}
    public Guid ExpectedStamp {get;set;}
}
public sealed class SubmitLiveSoloWriteUpValidator:Validator<SubmitLiveSoloWriteUpRequest>
{
    public SubmitLiveSoloWriteUpValidator()
    { RuleFor(x=>x.CompetitionId).NotEmpty();RuleFor(x=>x.MatchId).NotEmpty();RuleFor(x=>x.RoundId).NotEmpty();RuleFor(x=>x.QuestionId).NotEmpty();RuleFor(x=>x.ExpectedStamp).NotEmpty(); }
}
public sealed class SubmitLiveSoloWriteUpEndpoint(ManageLiveSoloWriteUps writeUps,IUserContext user,TimeProvider clock)
    :Endpoint<SubmitLiveSoloWriteUpRequest,Results<Ok<ChallengeWriteUpResponse>,NotFound,ForbidHttpResult,Conflict<ChallengeWriteUpFailureResponse>,UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/writeups/submit");AuthSchemes("Bearer");
        Description(x=>x.WithName("SubmitLiveSoloWriteUp"));Summary(x=>x.Summary="Submits a post-event scoped solution version for review without a gameplay action.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpResponse>,NotFound,ForbidHttpResult,Conflict<ChallengeWriteUpFailureResponse>,UnprocessableEntity<ChallengeWriteUpFailureResponse>>> ExecuteAsync(SubmitLiveSoloWriteUpRequest req,CancellationToken ct)
        =>ChallengeWriteUpProtocol.Mutation(await writeUps.SubmitAsync(new(req.CompetitionId,req.MatchId,req.RoundId,req.QuestionId,user.UserId,clock.GetUtcNow()),req.Official,req.ExpectedStamp,ct));
}
