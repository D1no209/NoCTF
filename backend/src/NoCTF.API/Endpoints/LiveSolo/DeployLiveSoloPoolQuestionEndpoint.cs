using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Templates;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class DeployLiveSoloPoolQuestionRequest
{
    public Guid CompetitionId { get; set; }
    public Guid SourceTemplateId { get; set; }
    public Guid ExpectedSourceStamp { get; set; }
}
public sealed class DeployLiveSoloPoolQuestionValidator : Validator<DeployLiveSoloPoolQuestionRequest>
{
    public DeployLiveSoloPoolQuestionValidator()
    { RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.SourceTemplateId).NotEmpty(); RuleFor(x => x.ExpectedSourceStamp).NotEmpty(); }
}
public sealed class DeployLiveSoloPoolQuestionEndpoint(ManageLiveSoloQuestionPool pool, IUserContext user, TimeProvider clock)
    : Endpoint<DeployLiveSoloPoolQuestionRequest, Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/question-pool/deploy"); AuthSchemes("Bearer");
        Description(x => x.WithName("DeployLiveSoloPoolQuestion"));
        Summary(x => x.Summary = "Adds an applicable LiveSolo template once, following the bank until competition content is edited.");
    }
    public override async Task<Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>>> ExecuteAsync(DeployLiveSoloPoolQuestionRequest req, CancellationToken ct) =>
        LiveSoloPoolProtocol.Mutation(await pool.DeployAsync(new(req.CompetitionId, user.UserId, req.SourceTemplateId, req.ExpectedSourceStamp, clock.GetUtcNow()), ct));
}
