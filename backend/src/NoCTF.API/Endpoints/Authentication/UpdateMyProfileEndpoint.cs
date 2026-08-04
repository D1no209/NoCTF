using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class UpdateMyProfileRequest
{
    public string? Description { get; set; }
}

public sealed class UpdateMyProfileValidator : Validator<UpdateMyProfileRequest>
{
    public UpdateMyProfileValidator() =>
        RuleFor(request => request.Description)
            .MaximumLength(UserProfileRules.MaximumDescriptionLength);
}

public sealed class UpdateMyProfileEndpoint(
    UpdateCurrentUserProfile update,
    IUserContext user,
    LinkGenerator links)
    : Endpoint<UpdateMyProfileRequest, Results<Ok<CurrentUserResponse>, NotFound>>
{
    public override void Configure()
    {
        Put("/auth/me/profile");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("Authentication_UpdateMyProfile"));
        Summary(summary => summary.Summary = "Updates the current user's public description.");
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound>> ExecuteAsync(
        UpdateMyProfileRequest request,
        CancellationToken ct)
    {
        var profile = await update.ExecuteAsync(
            user.UserId,
            request.Description,
            DateTimeOffset.UtcNow,
            ct);
        return profile is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CurrentUserMapping.ToResponse(profile, links, HttpContext));
    }
}
