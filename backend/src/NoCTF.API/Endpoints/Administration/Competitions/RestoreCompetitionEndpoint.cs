using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class RestoreCompetitionEndpoint(
    RestoreCompetition restore,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<
            NoContent,
            NotFound,
            Conflict<CompetitionResourceManagerConflictResponse>>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRestoreCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Restores a soft-deleted competition.";
            summary.Description = "Restores a competition when the caller is its owner or a platform administrator.";
        });
    }

    public override async Task<
        Results<
            NoContent,
            NotFound,
            Conflict<CompetitionResourceManagerConflictResponse>>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await restore.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            DateTimeOffset.UtcNow,
            ct);
        return result.State switch
        {
            CompetitionRestoreState.Restored => TypedResults.NoContent(),
            CompetitionRestoreState.NotFound => TypedResults.NotFound(),
            CompetitionRestoreState.RevisionConflict =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.RevisionConflict,
                        null)),
            CompetitionRestoreState.UserNotFound =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.UserNotFound,
                        result.UserIds)),
            CompetitionRestoreState.RoleNotEligible =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.RoleNotEligible,
                        result.UserIds)),
            _ => throw new InvalidOperationException(
                $"Unsupported competition restore state: {result.State}.")
        };
    }
}
