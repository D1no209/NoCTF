using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class PreviewChallengeTimingRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public CompetitionChallengeTimingPatchRequest Timing { get; set; } = new();
}
public sealed class PreviewChallengeTimingEndpoint(GetChallenge get, ManageChallengeTiming timing,
    ICompetitionModerationAuthorizer access, IUserContext user, TimeProvider clock)
    : Endpoint<PreviewChallengeTimingRequest, Results<Ok<ChallengeTimingPreview>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/timing-preview");
        AuthSchemes("Bearer"); Description(x => x.WithName("AdminPreviewCompetitionChallengeTiming"));
        Summary(summary =>
        {
            summary.Summary = "Previews a competition challenge timing change.";
            summary.Description = "Returns current and proposed scores, blood awards and progression impact without writing facts. The confirmation token expires after two minutes and is invalidated by related changes.";
        });
    }
    public override async Task<Results<Ok<ChallengeTimingPreview>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(PreviewChallengeTimingRequest req, CancellationToken ct)
    {
        if (!await access.CanModerateAsync(user.UserId, req.CompetitionId, ct)) return TypedResults.Forbid();
        var current = await get.ExecuteAsync(req.CompetitionId, req.CompetitionChallengeId, true, false, ct);
        if (current is null) return TypedResults.NotFound();
        var candidate = req.Timing.Apply(current.Timing);
        var failure = await timing.ValidateCandidateAsync(new(req.CompetitionId, req.CompetitionChallengeId, candidate, clock.GetUtcNow()), ct);
        if (failure == ChallengeTimingFailure.NotFound) return TypedResults.NotFound();
        if (failure is not null)
            return ApiProblems.Problem(statusCode: 400, detail: ApiMessages.For(failure),
                extensions: new Dictionary<string, object?> { ["code"] = failure.Value });
        var preview = await timing.PreviewAsync(new(req.CompetitionId, req.CompetitionChallengeId,
            candidate, clock.GetUtcNow()), user.UserId, ct);
        return preview is null ? TypedResults.NotFound() : TypedResults.Ok(preview);
    }
}
