using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges.WriteUps;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloWriteUpsRequest
{
    public Guid CompetitionId {get;set;}
    public Guid MatchId {get;set;}
    public Guid RoundId {get;set;}
    public Guid QuestionId {get;set;}
    public bool Staff {get;set;}
}
public sealed class ListLiveSoloWriteUpsEndpoint(ManageLiveSoloWriteUps writeUps,IUserContext user,TimeProvider clock)
    : Endpoint<ListLiveSoloWriteUpsRequest,Results<Ok<ChallengeWriteUpListResponse>,NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/writeups");AuthSchemes("Bearer");
        Description(x=>x.WithName("ListLiveSoloWriteUps"));Summary(x=>x.Summary="Lists post-event scoped solution metadata without any unlock or score action.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpListResponse>,NotFound>> ExecuteAsync(ListLiveSoloWriteUpsRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        var result=await writeUps.ListAsync(new(req.CompetitionId,req.MatchId,req.RoundId,req.QuestionId,user.UserId,clock.GetUtcNow()),req.Staff,ct);
        return result is null?TypedResults.NotFound():TypedResults.Ok(new ChallengeWriteUpListResponse(result.Access,result.Items.Select(ChallengeWriteUpProtocol.Map).ToArray()));
    }
}
