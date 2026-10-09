using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges.WriteUps;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SaveLiveSoloWriteUpDraftRequest
{
    public Guid CompetitionId {get;set;}
    public Guid MatchId {get;set;}
    public Guid RoundId {get;set;}
    public Guid QuestionId {get;set;}
    public bool Official {get;set;}
    public Guid? ExpectedStamp {get;set;}
    public string Markdown {get;set;}="";
}
public sealed class SaveLiveSoloWriteUpDraftValidator:Validator<SaveLiveSoloWriteUpDraftRequest>
{
    public SaveLiveSoloWriteUpDraftValidator()
    { RuleFor(x=>x.CompetitionId).NotEmpty();RuleFor(x=>x.MatchId).NotEmpty();RuleFor(x=>x.RoundId).NotEmpty();RuleFor(x=>x.QuestionId).NotEmpty();RuleFor(x=>x.Markdown).NotEmpty().MaximumLength(ChallengeWriteUpPolicy.MaximumMarkdownCharacters); }
}
public sealed class SaveLiveSoloWriteUpDraftEndpoint(ManageLiveSoloWriteUps writeUps,IUserContext user,TimeProvider clock)
    :Endpoint<SaveLiveSoloWriteUpDraftRequest,Results<Ok<ChallengeWriteUpResponse>,NotFound,ForbidHttpResult,Conflict<ChallengeWriteUpFailureResponse>,UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/writeups/draft");AuthSchemes("Bearer");
        Description(x=>x.WithName("SaveLiveSoloWriteUpDraft"));Summary(x=>x.Summary="Saves a post-event Markdown solution through a previously opened question scope.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpResponse>,NotFound,ForbidHttpResult,Conflict<ChallengeWriteUpFailureResponse>,UnprocessableEntity<ChallengeWriteUpFailureResponse>>> ExecuteAsync(SaveLiveSoloWriteUpDraftRequest req,CancellationToken ct)
        =>ChallengeWriteUpProtocol.Mutation(await writeUps.SaveMarkdownAsync(new(req.CompetitionId,req.MatchId,req.RoundId,req.QuestionId,user.UserId,clock.GetUtcNow()),req.Official,req.Markdown,req.ExpectedStamp,ct));
}
