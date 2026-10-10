using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Templates;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ExportLiveSoloPoolQuestionRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public Guid ExpectedMaterialStamp { get; set; }
}
public sealed class ExportLiveSoloPoolQuestionValidator : Validator<ExportLiveSoloPoolQuestionRequest>
{
    public ExportLiveSoloPoolQuestionValidator()
    { RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.CompetitionChallengeId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty(); RuleFor(x => x.ExpectedMaterialStamp).NotEmpty(); }
}
public sealed record LiveSoloPoolExportResponse(Guid ChallengeId);
public sealed class ExportLiveSoloPoolQuestionEndpoint(ManageLiveSoloQuestionPool pool, IUserContext user, TimeProvider clock)
    : Endpoint<ExportLiveSoloPoolQuestionRequest, Results<Ok<LiveSoloPoolExportResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/question-pool/{competitionChallengeId}/save-to-bank"); AuthSchemes("Bearer");
        Description(x => x.WithName("ExportLiveSoloPoolQuestion"));
        Summary(x => x.Summary = "Copies competition-owned material to a new independent question-bank template without relinking the competition.");
    }
    public override async Task<Results<Ok<LiveSoloPoolExportResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>>> ExecuteAsync(ExportLiveSoloPoolQuestionRequest req, CancellationToken ct)
    {
        var result = await pool.ExportAsync(new(req.CompetitionId, req.CompetitionChallengeId, user.UserId, req.ExpectedStamp, req.ExpectedMaterialStamp, clock.GetUtcNow()), ct);
        return result.Failure switch
        {
            null when result.ChallengeId is Guid id => TypedResults.Ok(new LiveSoloPoolExportResponse(id)),
            LiveSoloPoolFailure.NotFound => TypedResults.NotFound(), LiveSoloPoolFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloPoolFailure.Conflict or LiveSoloPoolFailure.ActiveRound => TypedResults.Conflict(new LiveSoloPoolFailureResponse(result.Failure.Value)),
            _ => TypedResults.UnprocessableEntity(new LiveSoloPoolFailureResponse(result.Failure ?? LiveSoloPoolFailure.InvalidContent))
        };
    }
}
