using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Storage;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Hosting.Observability;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed class UploadMyTeamWriteUpRequest
{
    public IFormFile File { get; set; } = default!;
}

public sealed class UploadMyTeamWriteUpValidator : Validator<UploadMyTeamWriteUpRequest>
{
    public UploadMyTeamWriteUpValidator()
    {
        RuleFor(request => request.File).NotNull();
        RuleFor(request => request.File.Length).GreaterThan(0)
            .When(request => request.File is not null);
        RuleFor(request => request.File.FileName)
            .Must(TeamWriteUpRules.HasValidFileName)
            .When(request => request.File is not null)
            .WithMessage("WriteUp file names must use the .pdf extension.");
        RuleFor(request => request.File.ContentType)
            .Must(TeamWriteUpRules.HasValidContentType)
            .When(request => request.File is not null)
            .WithMessage("WriteUp files must declare application/pdf.");
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamWriteUpFailureCode>))]
public enum TeamWriteUpFailureCode
{
    InvalidPdf,
    UploadTooLarge,
    WriteUpSubmissionDeadlinePassed
}

public sealed record TeamWriteUpFailureResponse(
    TeamWriteUpFailureCode Code,
    string Detail);

public sealed class UploadMyTeamWriteUpEndpoint(
    ManageTeamWriteUps writeUps,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<UploadMyTeamWriteUpRequest,
        Results<Ok<TeamWriteUpResponse>, NotFound, ForbidHttpResult,
            Conflict<TeamWriteUpFailureResponse>,
            UnprocessableEntity<TeamWriteUpFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/teams/me/writeup");
        AuthSchemes("Bearer");
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            TeamWriteUpRules.MaximumFileBytes));
        Options(builder => builder.WithMetadata(new ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Upload)));
        Description(builder => builder
            .WithName("ReplaceMyTeamWriteUp")
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status413PayloadTooLarge));
        Summary(summary =>
        {
            summary.Summary = "Submits or replaces the current team's PDF WriteUp.";
            summary.Description = "Any active member of an approved, non-banned team may submit or replace its required or optional PDF through the configured deadline. The previous immutable File is cleaned up asynchronously.";
        });
    }

    public override async Task<Results<Ok<TeamWriteUpResponse>, NotFound,
        ForbidHttpResult, Conflict<TeamWriteUpFailureResponse>,
        UnprocessableEntity<TeamWriteUpFailureResponse>,
        ProblemHttpResult>> ExecuteAsync(
        UploadMyTeamWriteUpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File.Length > TeamWriteUpRules.MaximumFileBytes)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "WriteUp is too large.",
                detail: $"WriteUp PDFs cannot exceed {TeamWriteUpRules.MaximumFileBytes} bytes.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = TeamWriteUpFailureCode.UploadTooLarge
                });
        }

        await using var content = request.File.OpenReadStream();
        var result = await writeUps.ReplaceMineAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            request.File.FileName,
            request.File.ContentType,
            request.File.Length,
            content,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return result.State switch
        {
            TeamWriteUpSubmissionState.Updated => TypedResults.Ok(
                TeamWriteUpProtocol.ToResponse(result.WriteUp!)),
            TeamWriteUpSubmissionState.NotFound => TypedResults.NotFound(),
            TeamWriteUpSubmissionState.Forbidden => TypedResults.Forbid(),
            TeamWriteUpSubmissionState.SubmissionDeadlinePassed => TypedResults.Conflict(
                new TeamWriteUpFailureResponse(
                    TeamWriteUpFailureCode.WriteUpSubmissionDeadlinePassed,
                    "The WriteUp submission deadline has passed.")),
            TeamWriteUpSubmissionState.InvalidPdf => TypedResults.UnprocessableEntity(
                new TeamWriteUpFailureResponse(
                    TeamWriteUpFailureCode.InvalidPdf,
                    "The upload is not a valid PDF document.")),
            _ => throw new InvalidOperationException(
                $"Unexpected WriteUp submission state {result.State}.")
        };
    }
}
