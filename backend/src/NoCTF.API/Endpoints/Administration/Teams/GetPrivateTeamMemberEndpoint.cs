using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.Application.Authentication.Privacy;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class GetPrivateTeamMemberEndpoint(AccountPrivacy privacy, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<PrivateAccountResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/teams/{teamId}/members/{userId}/private-profile"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetPrivateTeamMember"));
        Summary(summary => { summary.Summary = "Reads a participant's private identity and this competition's source-IP activity.";
            summary.Description = "Owner, Manager, Judge and platform administrators only; Observer and other participants are denied. All route scopes are checked. Authentication history is never included."; });
    }
    public override async Task<Results<Ok<PrivateAccountResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var value = await privacy.ReadMemberAsync(user.UserId, Route<Guid>("competitionId"), Route<Guid>("teamId"), Route<Guid>("userId"), ct);
        return value is null ? TypedResults.NotFound() : TypedResults.Ok(PrivateAccountResponse.From(value));
    }
}
