using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class UnlockChallengeWriteUpRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid VersionId { get; set; }
    public Guid PolicyStamp { get; set; }
}
public sealed class UnlockChallengeWriteUpValidator : Validator<UnlockChallengeWriteUpRequest>
{
    public UnlockChallengeWriteUpValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.CompetitionChallengeId).NotEmpty();
        RuleFor(x => x.VersionId).NotEmpty(); RuleFor(x => x.PolicyStamp).NotEmpty();
    }
}
public sealed record ChallengeWriteUpUnlockResponse(bool Created, int DeductionPercent);
public sealed class UnlockChallengeWriteUpEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<UnlockChallengeWriteUpRequest, Results<Ok<ChallengeWriteUpUnlockResponse>, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/versions/{versionId}/unlock"); AuthSchemes("Bearer");
        Description(x => x.WithName("UnlockChallengeWriteUp"));
        Summary(x => x.Summary = "Explicitly confirms the current policy and public version before recording one team-shared formal unlock.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpUnlockResponse>, ForbidHttpResult,
        Conflict<ChallengeWriteUpFailureResponse>>> ExecuteAsync(UnlockChallengeWriteUpRequest request, CancellationToken ct)
    {
        var result = await writeUps.UnlockAsync(new(request.CompetitionId, request.CompetitionChallengeId, request.VersionId,
            user.UserId, request.PolicyStamp, clock.GetUtcNow()), ct);
        if (result.Failure == ChallengeWriteUpFailure.Forbidden) return TypedResults.Forbid();
        if (result.Failure is { } failure) return TypedResults.Conflict(new ChallengeWriteUpFailureResponse(failure));
        return TypedResults.Ok(new ChallengeWriteUpUnlockResponse(result.Created, result.DeductionPercent));
    }
}
