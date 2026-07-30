using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class GetChallengeTemplateRequest
{
    public Guid ChallengeId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class GetChallengeTemplateEndpoint(
    GetChallengeTemplate get,
    IUserContext user)
    : Endpoint<GetChallengeTemplateRequest, Results<Ok<ChallengeTemplateResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/challenges/{challengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankGetTemplate"));
        Summary(summary =>
        {
            summary.Summary = "Gets a global challenge template.";
            summary.Description = "Returns reusable template metadata according to challenge-bank visibility rules.";
        });
    }

    public override async Task<Results<Ok<ChallengeTemplateResponse>, NotFound>> ExecuteAsync(
        GetChallengeTemplateRequest request,
        CancellationToken ct)
    {
        var result = await get.ExecuteAsync(
            request.ChallengeId,
            user.UserId,
            user.IsAdministrator,
            request.IncludeDeleted,
            ct: ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeTemplateMapper.ToResponse(result));
    }
}
