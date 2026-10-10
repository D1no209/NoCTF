using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloWriteUpFileRequest
{
    public Guid CompetitionId {get;set;}
    public Guid MatchId {get;set;}
    public Guid RoundId {get;set;}
    public Guid QuestionId {get;set;}
    public Guid VersionId {get;set;}
    public bool Staff {get;set;}
    public bool Download {get;set;}
}
public sealed class GetLiveSoloWriteUpFileEndpoint(ManageLiveSoloWriteUps writeUps,IUserContext user,TimeProvider clock)
    :Endpoint<GetLiveSoloWriteUpFileRequest,Results<FileStreamHttpResult,NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/writeups/versions/{versionId}/file");AuthSchemes("Bearer");
        Options(x=>x.WithMetadata(new WriteUpBrowserAccessMetadata()));Description(x=>x.WithName("GetLiveSoloWriteUpFile"));
        Summary(x=>x.Summary="Streams a post-event scoped PDF through current JWT qualification, publication and immutable file binding.");
    }
    public override async Task<Results<FileStreamHttpResult,NotFound>> ExecuteAsync(GetLiveSoloWriteUpFileRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";HttpContext.Response.Headers["X-Content-Type-Options"]="nosniff";
        var file=await writeUps.OpenPdfAsync(new(req.CompetitionId,req.MatchId,req.RoundId,req.QuestionId,user.UserId,clock.GetUtcNow()),req.VersionId,req.Staff,ct);
        if (file is null) return TypedResults.NotFound();
        if (!WriteUpBrowserAccess.MatchesFile(HttpContext,file.FileId)) {await file.Content.DisposeAsync();return TypedResults.NotFound();}
        return TypedResults.Stream(file.Content,"application/pdf",req.Download?file.FileName:null,enableRangeProcessing:true);
    }
}
