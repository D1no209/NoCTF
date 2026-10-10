using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges.WriteUps;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloWriteUpContentRequest
{
    public Guid CompetitionId {get;set;}
    public Guid MatchId {get;set;}
    public Guid RoundId {get;set;}
    public Guid QuestionId {get;set;}
    public Guid VersionId {get;set;}
    public bool Staff {get;set;}
}
public sealed class GetLiveSoloWriteUpContentEndpoint(ManageLiveSoloWriteUps writeUps,IUserContext user,TimeProvider clock)
    :Endpoint<GetLiveSoloWriteUpContentRequest,Results<Ok<ChallengeWriteUpContentResponse>,NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/writeups/versions/{versionId}");AuthSchemes("Bearer");
        Description(x=>x.WithName("GetLiveSoloWriteUpContent"));Summary(x=>x.Summary="Reads post-event scoped solution text without creating an unlock receipt or applying a deduction.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpContentResponse>,NotFound>> ExecuteAsync(GetLiveSoloWriteUpContentRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        var result=await writeUps.ReadAsync(new(req.CompetitionId,req.MatchId,req.RoundId,req.QuestionId,user.UserId,clock.GetUtcNow()),req.VersionId,req.Staff,ct);
        return result.Failure is not null?TypedResults.NotFound():TypedResults.Ok(new ChallengeWriteUpContentResponse(result.VersionId,result.Format,result.Markdown,result.File?.FileName));
    }
}
