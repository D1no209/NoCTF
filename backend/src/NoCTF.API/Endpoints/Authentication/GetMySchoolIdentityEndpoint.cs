using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Privacy;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record SchoolIdentityResponse(string? FullName, string? StudentNumber, int? IpRetentionDays = null);

public sealed class GetMySchoolIdentityEndpoint(AccountPrivacy privacy, IUserContext user,
    Microsoft.Extensions.Options.IOptions<AccountPrivacyOptions> options)
    : EndpointWithoutRequest<Results<Ok<SchoolIdentityResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/auth/me/school-identity"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AuthenticationGetMySchoolIdentity"));
        Summary(summary => summary.Summary = "Reads the current user's private, self-reported school identity.");
    }
    public override async Task<Results<Ok<SchoolIdentityResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var value = await privacy.GetOwnAsync(user.UserId, ct);
        return value is null ? TypedResults.NotFound() : TypedResults.Ok(new SchoolIdentityResponse(value.FullName, value.StudentNumber, options.Value.IpRetentionDays));
    }
}
