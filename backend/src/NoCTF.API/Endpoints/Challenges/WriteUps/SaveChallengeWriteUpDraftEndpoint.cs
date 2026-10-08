using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class SaveChallengeWriteUpDraftRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public bool Official { get; set; }
    public Guid? ExpectedStamp { get; set; }
    public string Markdown { get; set; } = string.Empty;
}
public sealed class SaveChallengeWriteUpDraftValidator : Validator<SaveChallengeWriteUpDraftRequest>
{
    public SaveChallengeWriteUpDraftValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.CompetitionChallengeId).NotEmpty();
        RuleFor(x => x.Markdown).NotEmpty().MaximumLength(ChallengeWriteUpPolicy.MaximumMarkdownCharacters);
    }
}
public sealed class SaveChallengeWriteUpDraftEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<SaveChallengeWriteUpDraftRequest, Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>, UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/draft"); AuthSchemes("Bearer");
        Description(x => x.WithName("SaveChallengeWriteUpDraft"));
        Summary(x => x.Summary = "Saves a team or official Markdown draft with optimistic concurrency.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>, UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
        ExecuteAsync(SaveChallengeWriteUpDraftRequest request, CancellationToken ct) =>
        ChallengeWriteUpProtocol.Mutation(await writeUps.SaveMarkdownAsync(new(request.CompetitionId,
            request.CompetitionChallengeId, user.UserId, request.Official, WriteUpFormat.Markdown,
            request.Markdown, null, request.ExpectedStamp, clock.GetUtcNow()), ct));
}
