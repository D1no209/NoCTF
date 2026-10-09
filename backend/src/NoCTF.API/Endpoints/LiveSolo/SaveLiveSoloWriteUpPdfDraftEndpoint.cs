using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges.WriteUps;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Teams.WriteUps;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SaveLiveSoloWriteUpPdfDraftRequest
{
    public Guid CompetitionId {get;set;}
    public Guid MatchId {get;set;}
    public Guid RoundId {get;set;}
    public Guid QuestionId {get;set;}
    public bool Official {get;set;}
    public Guid? ExpectedStamp {get;set;}
    public IFormFile? File {get;set;}
}
public sealed class SaveLiveSoloWriteUpPdfDraftValidator:Validator<SaveLiveSoloWriteUpPdfDraftRequest>
{
    public SaveLiveSoloWriteUpPdfDraftValidator()
    {
        RuleFor(x=>x.CompetitionId).NotEmpty();RuleFor(x=>x.MatchId).NotEmpty();RuleFor(x=>x.RoundId).NotEmpty();RuleFor(x=>x.QuestionId).NotEmpty();RuleFor(x=>x.File).NotNull();
        RuleFor(x=>x.File!.Length).InclusiveBetween(1,TeamWriteUpRules.MaximumFileBytes).When(x=>x.File is not null);
    }
}
public sealed class SaveLiveSoloWriteUpPdfDraftEndpoint(ManageLiveSoloWriteUps writeUps,IUserContext user,TimeProvider clock)
    :Endpoint<SaveLiveSoloWriteUpPdfDraftRequest,Results<Ok<ChallengeWriteUpResponse>,NotFound,ForbidHttpResult,Conflict<ChallengeWriteUpFailureResponse>,UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/writeups/draft/pdf");AuthSchemes("Bearer");AllowFileUploads();
        Description(x=>x.WithName("SaveLiveSoloWriteUpPdfDraft"));Summary(x=>x.Summary="Saves an immutable managed PDF draft after post-event question-scope qualification.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpResponse>,NotFound,ForbidHttpResult,Conflict<ChallengeWriteUpFailureResponse>,UnprocessableEntity<ChallengeWriteUpFailureResponse>>> ExecuteAsync(SaveLiveSoloWriteUpPdfDraftRequest req,CancellationToken ct)
    {
        await using var content=req.File!.OpenReadStream();
        return ChallengeWriteUpProtocol.Mutation(await writeUps.SavePdfAsync(new(req.CompetitionId,req.MatchId,req.RoundId,req.QuestionId,user.UserId,clock.GetUtcNow()),req.Official,req.ExpectedStamp,req.File.FileName,req.File.ContentType,req.File.Length,content,ct));
    }
}
