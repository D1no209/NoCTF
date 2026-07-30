using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record CurrentUserResponse(
    Guid UserId,
    string UserName,
    string Email,
    UserRole Role,
    UserKind Kind,
    bool EmailVerified);

public sealed class GetMeEndpoint(GetCurrentUser getCurrent, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<CurrentUserResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/auth/me");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Get the current user";
            summary.Description = "Returns the authenticated account profile.";
        });
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var profile = await getCurrent.ExecuteAsync(user.UserId, ct);
        return profile is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new CurrentUserResponse(
                profile.Id,
                profile.UserName,
                profile.Email,
                profile.Role,
                profile.Kind,
                profile.EmailVerified));
    }
}
