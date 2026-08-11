using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class HardDeleteCompetitionEndpoint(
    HardDeleteCompetition hardDelete,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<
            NoContent,
            NotFound,
            Conflict<CompetitionHardDeletePreviewResponse>>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/hard-delete");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminHardDeleteCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes an empty competition without history.";
            summary.Description =
                "Historical competition events and all other reported references block physical deletion. Soft deletion is a separate operation and is not a prerequisite.";
        });
    }

    public override async Task<
        Results<
            NoContent,
            NotFound,
            Conflict<CompetitionHardDeletePreviewResponse>>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await hardDelete.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            ct);
        return result.State switch
        {
            CompetitionHardDeleteState.Deleted => TypedResults.NoContent(),
            CompetitionHardDeleteState.NotFound => TypedResults.NotFound(),
            CompetitionHardDeleteState.Blocked when result.Preview is not null =>
                TypedResults.Conflict(
                    CompetitionHardDeleteMapping.ToResponse(result.Preview)),
            _ => throw new InvalidOperationException(
                $"Unsupported competition hard-delete state: {result.State}.")
        };
    }
}
