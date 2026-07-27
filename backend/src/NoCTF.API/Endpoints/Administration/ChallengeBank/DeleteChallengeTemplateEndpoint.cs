using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class DeleteChallengeTemplateEndpoint(
    DeleteChallengeTemplate delete,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Delete("/admin/challenges/{challengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankDeleteTemplate"));
        Summary(summary =>
        {
            summary.Summary = "Deletes a global challenge template.";
            summary.Description = "Soft-deletes an unreferenced reusable challenge template.";
        });
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var result = await delete.ExecuteAsync(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            restore: false,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
