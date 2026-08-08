using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
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
        RuleFor(request => request.File.Length).InclusiveBetween(1, 10 * 1024 * 1024)
            .When(request => request.File is not null);
        RuleFor(request => request.File.ContentType)
            .Must(value => ContentTypes.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(request => request.File is not null)
            .WithMessage("Avatar must be a JPEG, PNG, or WebP image.");
    }
}

public sealed record TeamAvatarResponse(Guid FileId, string ContentType);

public sealed class UploadTeamAvatarEndpoint(
    ManageBusinessImages images,
    IUserContext user)
    : Endpoint<UploadTeamAvatarRequest,
        Results<Ok<TeamAvatarResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/teams/{teamId}/avatar");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder.WithName("TeamAvatar_Replace"));
        Summary(summary => summary.Summary = "Replaces a team's avatar with an immutable File reference.");
    }

    public override async Task<Results<Ok<TeamAvatarResponse>, NotFound, ForbidHttpResult>>
        ExecuteAsync(UploadTeamAvatarRequest request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        var result = await images.ReplaceTeamAvatarAsync(
            user.UserId,
            user.IsAdministrator,
            Route<Guid>("competitionId"),
            Route<Guid>("teamId"),
            request.File.FileName,
            request.File.ContentType,
            content,
            DateTimeOffset.UtcNow,
            ct);
        return result.State switch
        {
            BusinessFileReferenceState.Updated => TypedResults.Ok(
                new TeamAvatarResponse(result.File!.FileId, result.File.ContentType)),
            BusinessFileReferenceState.NotFound => TypedResults.NotFound(),
            BusinessFileReferenceState.Forbidden => TypedResults.Forbid(),
            _ => throw new InvalidOperationException($"Unexpected avatar state {result.State}.")
        };
    }
}
