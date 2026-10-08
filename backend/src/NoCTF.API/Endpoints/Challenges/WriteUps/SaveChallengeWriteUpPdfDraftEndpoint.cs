using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Storage;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class SaveChallengeWriteUpPdfDraftRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public bool Official { get; set; }
    public Guid? ExpectedStamp { get; set; }
    public IFormFile File { get; set; } = default!;
}
public sealed class SaveChallengeWriteUpPdfDraftValidator : Validator<SaveChallengeWriteUpPdfDraftRequest>
{
    public SaveChallengeWriteUpPdfDraftValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.CompetitionChallengeId).NotEmpty();
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.Length).InclusiveBetween(1, TeamWriteUpRules.MaximumFileBytes).When(x => x.File is not null);
    }
}
public sealed class SaveChallengeWriteUpPdfDraftEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<SaveChallengeWriteUpPdfDraftRequest, Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>, UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/draft/pdf"); AuthSchemes("Bearer");
        AllowFileUploads(); MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(TeamWriteUpRules.MaximumFileBytes));
        Description(x => x.WithName("SaveChallengeWriteUpPdfDraft"));
        Summary(x => x.Summary = "Uploads a private single-challenge PDF draft.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>, UnprocessableEntity<ChallengeWriteUpFailureResponse>>>
        ExecuteAsync(SaveChallengeWriteUpPdfDraftRequest request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        return ChallengeWriteUpProtocol.Mutation(await writeUps.SavePdfAsync(new(request.CompetitionId,
            request.CompetitionChallengeId, user.UserId, request.Official, WriteUpFormat.Pdf, null, null,
            request.ExpectedStamp, clock.GetUtcNow()), request.File.FileName, request.File.ContentType, request.File.Length, content, ct));
    }
}
