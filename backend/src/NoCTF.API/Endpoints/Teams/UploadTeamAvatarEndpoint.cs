using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http;
using NoCTF.API.Security;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Teams;

public sealed class UploadTeamAvatarRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadTeamAvatarValidator : Validator<UploadTeamAvatarRequest>
{
    private static readonly string[] ContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public UploadTeamAvatarValidator()
    {
        RuleFor(request => request.File).NotNull();
        RuleFor(request => request.File.Length).GreaterThan(0)
            .When(request => request.File is not null);
        RuleFor(request => request.File.ContentType)
            .Must(value => ContentTypes.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(request => request.File is not null)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.UploadTeamAvatarValidationAvatarJpegPngWebp)).WithErrorCode(ApiMessages.Key(ApiMessageId.UploadTeamAvatarValidationAvatarJpegPngWebp));
    }
}

public sealed record TeamAvatarResponse(Guid FileId, string ContentType);

public sealed class UploadTeamAvatarEndpoint(
    ManageBusinessImages images,
    IUserContext user,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadTeamAvatarRequest,
        Results<Ok<TeamAvatarResponse>, NotFound, ForbidHttpResult,
            Conflict<TeamRegistrationFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Upload)));
        Put("/competitions/{competitionId}/teams/{teamId}/avatar");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder.Accepts<UploadTeamAvatarRequest>("multipart/form-data"));
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            uploadLimits.MaximumAvatarBytes));
        Description(builder => builder
            .WithName("TeamAvatar_Replace")
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status413PayloadTooLarge));
        Summary(summary => { summary.Summary = "Replaces a team's avatar with an immutable File reference."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<TeamAvatarResponse>, NotFound, ForbidHttpResult,
        Conflict<TeamRegistrationFailureResponse>, ProblemHttpResult>>
        ExecuteAsync(UploadTeamAvatarRequest request, CancellationToken ct)
    {
        if (request.File.Length > uploadLimits.MaximumAvatarBytes)
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: ApiMessages.Get(ApiMessageId.UploadTeamAvatarTitleAvatarTooLarge),
                detail: ApiMessages.Get(ApiMessageId.UploadSizeLimit, new Dictionary<string, object?> { ["maximumBytes"] = uploadLimits.MaximumAvatarBytes }),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = FileUploadFailureCode.UploadTooLarge.ToString()
                });
        await using var content = request.File.OpenReadStream();
        var result = await images.ReplaceTeamAvatarAsync(
            user.UserId,
            user.IsAdministrator,
            Route<Guid>("competitionId"),
            Route<Guid>("teamId"),
            request.File.FileName,
            request.File.ContentType,
            content,
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            BusinessFileReferenceState.Updated => TypedResults.Ok(
                new TeamAvatarResponse(result.File!.FileId, result.File.ContentType)),
            BusinessFileReferenceState.NotFound => TypedResults.NotFound(),
            BusinessFileReferenceState.Forbidden => TypedResults.Forbid(),
            BusinessFileReferenceState.Conflict => TypedResults.Conflict(
                new TeamRegistrationFailureResponse(
                    TeamMapper.ToProtocol(result.Failure!.Value),
                    "Team avatar was not updated.")),
            _ => throw new InvalidOperationException($"Unexpected avatar state {result.State}.")
        };
    }
}
