using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class CreateLiveSoloPoolQuestionRequest
{
    public Guid CompetitionId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Direction { get; set; }
    public required ChallengeDefinitionContract Definition { get; set; }
}
public sealed class CreateLiveSoloPoolQuestionValidator : Validator<CreateLiveSoloPoolQuestionRequest>
{
    public CreateLiveSoloPoolQuestionValidator()
    { RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.Title).NotEmpty().MaximumLength(160); RuleFor(x => x.Direction).NotEmpty().MaximumLength(96); }
}
public sealed class CreateLiveSoloPoolQuestionEndpoint(ManageLiveSoloQuestionPool pool, IUserContext user, TimeProvider clock)
    : Endpoint<CreateLiveSoloPoolQuestionRequest, Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/question-pool"); AuthSchemes("Bearer");
        Description(x => x.WithName("CreateLiveSoloPoolQuestion"));
        Summary(x => x.Summary = "Creates material exclusively owned by this competition, outside the question bank.");
    }
    public override async Task<Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>>> ExecuteAsync(CreateLiveSoloPoolQuestionRequest req, CancellationToken ct)
    {
        if (req.Definition.Mode != NoCTF.API.Endpoints.Competitions.GameModeProtocol.LiveSolo)
            return TypedResults.UnprocessableEntity(new LiveSoloPoolFailureResponse(LiveSoloPoolFailure.UnsupportedDefinition));
        var definition = (LiveSoloChallengeDefinition)ChallengeDefinitionContractMapper.ToDomain(Guid.Empty, GameMode.LiveSolo, req.Definition);
        return LiveSoloPoolProtocol.Mutation(await pool.CreateAsync(new(req.CompetitionId, user.UserId, req.Title, req.Description, req.Direction, definition, clock.GetUtcNow()), ct));
    }
}
