using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class EditLiveSoloPoolQuestionRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public Guid ExpectedMaterialStamp { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Direction { get; set; }
    public IReadOnlyList<SaveChallengeFlagRequest>? Flags { get; set; }
    public IReadOnlyList<Guid>? RemovedAttachmentIds { get; set; }
    public required ChallengeDefinitionContract Definition { get; set; }
}
public sealed class EditLiveSoloPoolQuestionValidator : Validator<EditLiveSoloPoolQuestionRequest>
{
    public EditLiveSoloPoolQuestionValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.CompetitionChallengeId).NotEmpty();
        RuleFor(x => x.ExpectedStamp).NotEmpty(); RuleFor(x => x.ExpectedMaterialStamp).NotEmpty();
        RuleForEach(x => x.Flags!).SetValidator(new SaveChallengeFlagValidator()).When(x => x.Flags is not null);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160); RuleFor(x => x.Direction).NotEmpty().MaximumLength(96);
    }
}
public sealed class EditLiveSoloPoolQuestionEndpoint(ManageLiveSoloQuestionPool pool, IUserContext user, TimeProvider clock)
    : Endpoint<EditLiveSoloPoolQuestionRequest, Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>>>
{
    public override void Configure()
    {
        Patch("/competitions/{competitionId}/live-solo/question-pool/{competitionChallengeId}"); AuthSchemes("Bearer");
        Description(x => x.WithName("EditLiveSoloPoolQuestion"));
        Summary(x => x.Summary = "Atomically detaches on the first material edit while retaining the competition question identity.");
    }
    public override async Task<Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>>> ExecuteAsync(EditLiveSoloPoolQuestionRequest req, CancellationToken ct)
    {
        if (req.Definition.Mode != NoCTF.API.Endpoints.Competitions.GameModeProtocol.LiveSolo)
            return TypedResults.UnprocessableEntity(new LiveSoloPoolFailureResponse(LiveSoloPoolFailure.UnsupportedDefinition));
        var definition = (LiveSoloChallengeDefinition)ChallengeDefinitionContractMapper.ToDomain(Guid.Empty, GameMode.LiveSolo, req.Definition);
        return LiveSoloPoolProtocol.Mutation(await pool.EditAsync(new(req.CompetitionId, req.CompetitionChallengeId, user.UserId,
            req.ExpectedStamp, req.ExpectedMaterialStamp, req.Title, req.Description, req.Direction, definition, clock.GetUtcNow(), req.Flags?.Select(x => new LiveSoloPoolFlagInput(x.Flag, x.MatchKind == ChallengeFlagMatchKindProtocol.Exact ? NoCTF.Domain.Challenges.ChallengeFlagMatchKind.Exact : NoCTF.Domain.Challenges.ChallengeFlagMatchKind.RegularExpression)).ToArray(), req.RemovedAttachmentIds), ct));
    }
}
