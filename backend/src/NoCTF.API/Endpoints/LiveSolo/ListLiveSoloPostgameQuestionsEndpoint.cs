using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloPostgameQuestionsRequest {public Guid CompetitionId {get;set;}public Guid MatchId {get;set;}}
public sealed record LiveSoloPostgameQuestionsResponse(IReadOnlyList<LiveSoloPostgameQuestion> Items);
public sealed class ListLiveSoloPostgameQuestionsEndpoint(ILiveSoloPostgameQuestionAccess access,IUserContext user)
    :Endpoint<ListLiveSoloPostgameQuestionsRequest,Results<Ok<LiveSoloPostgameQuestionsResponse>,NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/postgame-questions");AuthSchemes("Bearer");
        Description(x=>x.WithName("ListLiveSoloPostgameQuestions"));Summary(x=>x.Summary="Lists historically opened questions for scoped post-event solution workflows.");
    }
    public override async Task<Results<Ok<LiveSoloPostgameQuestionsResponse>,NotFound>> ExecuteAsync(ListLiveSoloPostgameQuestionsRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";var result=await access.ListAsync(req.CompetitionId,req.MatchId,user.UserId,ct);
        return result is null?TypedResults.NotFound():TypedResults.Ok(new LiveSoloPostgameQuestionsResponse(result));
    }
}
