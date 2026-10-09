using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Adjudication;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class PreviewLiveSoloResultCorrectionRequest
{
    public Guid CompetitionId {get;set;} public Guid MatchId {get;set;} public Guid WinnerTeamId {get;set;}
    public int LeftWins {get;set;} public int RightWins {get;set;}
}
public sealed class PreviewLiveSoloResultCorrectionValidator:Validator<PreviewLiveSoloResultCorrectionRequest>
{
    public PreviewLiveSoloResultCorrectionValidator() {RuleFor(x=>x.CompetitionId).NotEmpty();RuleFor(x=>x.MatchId).NotEmpty();RuleFor(x=>x.WinnerTeamId).NotEmpty();
        RuleFor(x=>x.LeftWins).InclusiveBetween(0,1024);RuleFor(x=>x.RightWins).InclusiveBetween(0,1024);}
}
public sealed record LiveSoloCorrectionPreviewResponse(Guid PreviewId,Guid MatchStamp,Guid PreviousWinnerTeamId,
    int PreviousLeftWins,int PreviousRightWins,IReadOnlyList<LiveSoloCorrectionImpactResponse> Downstream);
public sealed class PreviewLiveSoloResultCorrectionEndpoint(ILiveSoloResultCorrectionStore corrections,IUserContext user)
    :Endpoint<PreviewLiveSoloResultCorrectionRequest,Results<Ok<LiveSoloCorrectionPreviewResponse>,NotFound>>
{
    public override void Configure(){Post("/competitions/{competitionId}/live-solo/matches/{matchId}/corrections/preview");AuthSchemes("Bearer");
        Description(x=>x.WithName("PreviewLiveSoloResultCorrection"));}
    public override async Task<Results<Ok<LiveSoloCorrectionPreviewResponse>,NotFound>> ExecuteAsync(PreviewLiveSoloResultCorrectionRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        var result=await corrections.PreviewAsync(new(req.CompetitionId,req.MatchId,user.UserId,req.WinnerTeamId,req.LeftWins,req.RightWins),ct);
        return result is null?TypedResults.NotFound():TypedResults.Ok(new LiveSoloCorrectionPreviewResponse(result.PreviewId,result.MatchStamp,result.PreviousWinnerTeamId,
            result.PreviousLeftWins,result.PreviousRightWins,result.Downstream.Select(LiveSoloCorrectionImpactResponse.From).ToArray()));
    }
}
