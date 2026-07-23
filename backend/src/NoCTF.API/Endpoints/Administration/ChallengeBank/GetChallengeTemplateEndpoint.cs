using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class GetChallengeTemplateEndpoint(
    GetChallengeTemplate get,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeTemplateResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/challenges/{challengeId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Gets a global challenge template.");
    }

    public override async Task<Results<Ok<ChallengeTemplateResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await get.ExecuteAsync(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            ct: ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeTemplateMapper.ToResponse(result));
    }
}
