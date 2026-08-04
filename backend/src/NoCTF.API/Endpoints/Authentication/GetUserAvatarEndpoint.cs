using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class GetUserAvatarEndpoint(GetUserAvatar getAvatar)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/users/{userId}/avatar");
        AllowAnonymous();
        Description(builder => builder.WithName("UserAvatar_Get"));
        Summary(summary => summary.Summary = "Returns a user's current public avatar.");
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var avatar = await getAvatar.ExecuteAsync(Route<Guid>("userId"), ct);
        return avatar is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(avatar.Content, avatar.ContentType);
    }
}
