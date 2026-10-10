using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Adjudication;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed record LiveSoloReplayConsentRequest(Guid MatchId,Guid ConcurrencyStamp);
public sealed class ResolveLiveSoloResultCorrectionRequest
{
    public Guid CompetitionId {get;set;} public Guid MatchId {get;set;} public Guid CorrectionId {get;set;}
    public Guid ExpectedStamp {get;set;} public required bool Apply {get;set;} public string Reason {get;set;}="";
    public IReadOnlyList<LiveSoloReplayConsentRequest> Replays {get;set;}=[];
}
public sealed class ResolveLiveSoloResultCorrectionValidator:Validator<ResolveLiveSoloResultCorrectionRequest>
{
    public ResolveLiveSoloResultCorrectionValidator(){RuleFor(x=>x.CompetitionId).NotEmpty();RuleFor(x=>x.MatchId).NotEmpty();RuleFor(x=>x.CorrectionId).NotEmpty();
        RuleFor(x=>x.ExpectedStamp).NotEmpty();RuleFor(x=>x.Reason).NotEmpty().MaximumLength(4000);RuleFor(x=>x.Replays).NotNull().Must(x=>x is not null && x.Count<=4096);
        RuleForEach(x=>x.Replays).NotNull();}
}
public sealed class ResolveLiveSoloResultCorrectionEndpoint(ManageLiveSoloResultCorrections corrections,IUserContext user)
    :Endpoint<ResolveLiveSoloResultCorrectionRequest,Results<Ok<LiveSoloResultCorrectionResponse>,NotFound,ForbidHttpResult,Conflict<LiveSoloFailureResponse>,UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure(){Post("/competitions/{competitionId}/live-solo/matches/{matchId}/corrections/{correctionId}/resolve");AuthSchemes("Bearer");
        Description(x=>x.WithName("ResolveLiveSoloResultCorrection"));}
    public override async Task<Results<Ok<LiveSoloResultCorrectionResponse>,NotFound,ForbidHttpResult,Conflict<LiveSoloFailureResponse>,UnprocessableEntity<LiveSoloFailureResponse>>>
        ExecuteAsync(ResolveLiveSoloResultCorrectionRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        return LiveSoloResultCorrectionResponse.Outcome(await corrections.ResolveAsync(new(req.CompetitionId,req.MatchId,user.UserId,req.CorrectionId,
            req.ExpectedStamp,req.Apply,req.Reason,req.Replays.Select(x=>new LiveSoloReplayConsent(x.MatchId,x.ConcurrencyStamp)).ToArray()),ct));
    }
}
