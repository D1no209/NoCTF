using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SubmitLiveSoloFixRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
    public IFormFile File { get; set; } = null!;
}
public sealed class SubmitLiveSoloFixValidator : Validator<SubmitLiveSoloFixRequest>
{
    public SubmitLiveSoloFixValidator()
    {
        RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.MatchId).NotEmpty(); RuleFor(value => value.RoundId).NotEmpty();
        RuleFor(value => value.QuestionId).NotEmpty(); RuleFor(value => value.RuntimeInstanceId).NotEmpty(); RuleFor(value => value.File).NotNull();
    }
}
public sealed record LiveSoloFixAcceptedResponse(Guid PatchUploadId, Guid GameplayFactId, long AdmissionSequence, DateTimeOffset AcceptedAt, string StatusUrl);
public sealed class SubmitLiveSoloFixEndpoint(SubmitLiveSoloFix submit, IUserContext user, TimeProvider clock)
    : Endpoint<SubmitLiveSoloFixRequest, Results<Accepted<LiveSoloFixAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/fix-targets/{runtimeInstanceId}/fixes");
        AuthSchemes("Bearer"); AllowFileUploads(); MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(PatchUploadRules.HardMaximumArchiveBytes));
        Description(builder => builder.WithName("SubmitLiveSoloFix"));
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.PatchUpload), new EnableRateLimitingAttribute("submission"),
            new HumanVerificationMetadata(HumanVerificationAction.Evaluation)));
    }
    public override async Task<Results<Accepted<LiveSoloFixAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(SubmitLiveSoloFixRequest request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        var result = await submit.ExecuteAsync(new(request.CompetitionId, request.MatchId, request.RoundId, request.QuestionId, user.UserId, clock.GetUtcNow()),
            request.RuntimeInstanceId, request.File.FileName, request.File.ContentType, content, ct);
        if (result.Upload is { } upload && result.Admission is { Failure: null, Sequence: { } sequence, AcceptedAt: { } at })
            return TypedResults.Accepted((string?)null, new LiveSoloFixAcceptedResponse(upload.PatchUploadId, upload.GameplayFactId, sequence, at,
                $"/api/v1/competitions/{request.CompetitionId}/gameplay-facts/{upload.GameplayFactId}"));
        if (result.Failure is null or PatchUploadFailureCode.PatchUploadNotAvailable) return TypedResults.NotFound();
        var status = result.Failure == PatchUploadFailureCode.ArchiveTooLarge ? 413
            : result.Failure is PatchUploadFailureCode.ArchiveInvalid or PatchUploadFailureCode.ArchiveStreamNotSeekable ? 422 : 409;
        return ApiProblems.Problem(statusCode: status, detail: ApiMessages.For(result.Failure.Value),
            extensions: new Dictionary<string, object?> { ["code"] = result.Failure.Value.ToString() });
    }
}
