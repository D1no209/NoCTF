using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges.WriteUps;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class PrepareLiveSoloWriteUpBrowserAccessRequest
{
    public Guid CompetitionId {get;set;}
    public Guid MatchId {get;set;}
    public Guid RoundId {get;set;}
    public Guid QuestionId {get;set;}
    public Guid VersionId {get;set;}
    public bool Staff {get;set;}
}
public sealed class PrepareLiveSoloWriteUpBrowserAccessEndpoint(ManageLiveSoloWriteUps writeUps,IUserContext user,TimeProvider clock,WriteUpBrowserAccess browser)
    :Endpoint<PrepareLiveSoloWriteUpBrowserAccessRequest,Results<Ok<WriteUpBrowserAccessResponse>,NotFound>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/writeups/versions/{versionId}/browser-access");AuthSchemes("Bearer");
        Description(x=>x.WithName("PrepareLiveSoloWriteUpBrowserAccess"));Summary(x=>x.Summary="Issues a short-lived exact-scope PDF grant for progressive preview and native browser download.");
    }
    public override async Task<Results<Ok<WriteUpBrowserAccessResponse>,NotFound>> ExecuteAsync(PrepareLiveSoloWriteUpBrowserAccessRequest req,CancellationToken ct)
    {
        var content=await writeUps.ReadAsync(new(req.CompetitionId,req.MatchId,req.RoundId,req.QuestionId,user.UserId,clock.GetUtcNow()),req.VersionId,req.Staff,ct);
        if (content.Failure is not null||content.Format!=WriteUpFormat.Pdf||content.File is null) return TypedResults.NotFound();
        var path=$"/api/v1/competitions/{req.CompetitionId}/live-solo/matches/{req.MatchId}/rounds/{req.RoundId}/questions/{req.QuestionId}/writeups/versions/{req.VersionId}/file";
        browser.Write(HttpContext,path,content.File.FileId);var query=req.Staff?"?staff=true":"?staff=false";
        return TypedResults.Ok(new WriteUpBrowserAccessResponse(path+query,path+query+"&download=true"));
    }
}
