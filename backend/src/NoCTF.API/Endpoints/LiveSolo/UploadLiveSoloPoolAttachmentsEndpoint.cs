using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class UploadLiveSoloPoolAttachmentsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public Guid ExpectedMaterialStamp { get; set; }
    public AttachmentDeliveryPolicyProtocol DeliveryPolicy { get; set; }
    public string? DownloadFileName { get; set; }
    public IReadOnlyList<IFormFile> Files { get; set; } = [];
}
public sealed class UploadLiveSoloPoolAttachmentsValidator : Validator<UploadLiveSoloPoolAttachmentsRequest>
{
    public UploadLiveSoloPoolAttachmentsValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.CompetitionChallengeId).NotEmpty();
        RuleFor(x => x.ExpectedStamp).NotEmpty(); RuleFor(x => x.ExpectedMaterialStamp).NotEmpty();
        RuleFor(x => x.DeliveryPolicy).IsInEnum(); RuleFor(x => x.Files).NotEmpty().Must(x => x.Count <= 128);
    }
}
public sealed class UploadLiveSoloPoolAttachmentsEndpoint(UploadLiveSoloPoolAttachments uploads, IUserContext user, FileUploadLimits limits, TimeProvider clock)
    : Endpoint<UploadLiveSoloPoolAttachmentsRequest, Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/question-pool/{competitionChallengeId}/attachments"); AuthSchemes("Bearer"); AllowFileUploads();
        Description(x => x.WithName("UploadLiveSoloPoolAttachments"));
    }
    public override async Task<Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>, ProblemHttpResult>> ExecuteAsync(UploadLiveSoloPoolAttachmentsRequest req, CancellationToken ct)
    {
        if (req.Files.Any(x => x.Length > limits.MaximumAttachmentBytes)) return TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge);
        var streams = req.Files.Select(x => x.OpenReadStream()).ToArray();
        try
        {
            var result = await uploads.ExecuteAsync(new(req.CompetitionId, req.CompetitionChallengeId, user.UserId, req.ExpectedStamp, req.ExpectedMaterialStamp,
                req.DeliveryPolicy == AttachmentDeliveryPolicyProtocol.RandomOnePerTeam ? AttachmentDeliveryPolicy.RandomOnePerTeam : AttachmentDeliveryPolicy.All,
                req.DownloadFileName, req.Files.Select((x, i) => new ChallengeAttachmentUploadItem(x.FileName, x.ContentType, streams[i])).ToArray(), clock.GetUtcNow()), ct);
            return result.Failure switch
            {
                null when result.Question is not null => TypedResults.Ok(LiveSoloPoolProtocol.Question(result.Question)),
                LiveSoloPoolFailure.NotFound => TypedResults.NotFound(),
                LiveSoloPoolFailure.Forbidden => TypedResults.Forbid(),
                LiveSoloPoolFailure.ProtectedMaterialsForbidden => ApiProblems.Problem(statusCode: StatusCodes.Status403Forbidden,
                    detail: ApiMessages.For(LiveSoloPoolFailure.ProtectedMaterialsForbidden), extensions: new Dictionary<string, object?> { ["code"] = LiveSoloPoolFailure.ProtectedMaterialsForbidden.ToString() }),
                LiveSoloPoolFailure.Conflict or LiveSoloPoolFailure.SourceChanged or LiveSoloPoolFailure.ActiveRound => TypedResults.Conflict(new LiveSoloPoolFailureResponse(result.Failure.Value)),
                _ => TypedResults.UnprocessableEntity(new LiveSoloPoolFailureResponse(result.Failure ?? LiveSoloPoolFailure.InvalidContent))
            };
        }
        finally { foreach (var stream in streams) await stream.DisposeAsync(); }
    }
}
