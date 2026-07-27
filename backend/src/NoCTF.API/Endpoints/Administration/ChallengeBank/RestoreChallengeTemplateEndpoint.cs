using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class RestoreChallengeTemplateEndpoint(
    DeleteChallengeTemplate restore,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankRestoreTemplate"));
        Summary(summary =>
        {
            summary.Summary = "Restores a soft-deleted global challenge template.";
            summary.Description = "Restores a reusable template for its owner or a platform administrator.";
        });
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var result = await restore.ExecuteAsync(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            restore: true,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
