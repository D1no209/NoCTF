using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed record ChallengeTemplateListResponse(IReadOnlyList<ChallengeTemplateResponse> Items);

public sealed class ListChallengeTemplatesEndpoint(
    ListChallengeTemplates list,
    IUserContext user)
    : EndpointWithoutRequest<Ok<ChallengeTemplateListResponse>>
{
    public override void Configure()
    {
        Get("/admin/challenges");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankListTemplates"));
        Summary(summary =>
        {
            summary.Summary = "Lists visible challenge templates.";
            summary.Description = "Returns owned, managed, shared, or platform-administrator-visible templates.";
        });
    }

    public override async Task<Ok<ChallengeTemplateListResponse>> ExecuteAsync(CancellationToken ct) =>
        TypedResults.Ok(ChallengeTemplateMapper.ToListResponse(
            await list.ExecuteAsync(user.UserId, user.IsAdministrator, ct)));
}
