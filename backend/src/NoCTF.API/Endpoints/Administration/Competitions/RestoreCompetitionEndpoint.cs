using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionRestoreConflictCodeProtocol>))]
public enum CompetitionRestoreConflictCodeProtocol
{
    UserNotFound,
    RoleNotEligible,
    ChallengeImageInvalid,
    RegistryAuthenticationRequired,
    RegistryAuthenticationFailed,
    RegistryUnavailable,
    RegistryManifestNotFound,
    RegistryManifestInvalid,
    ChallengeDefinitionRevisionConflict
}

public sealed record CompetitionRestoreConflictResponse(
    [property: Required, JsonRequired] CompetitionRestoreConflictCodeProtocol Code,
    [property: Required, JsonRequired] IReadOnlyList<Guid> UserIds,
    string? Detail = null);

public sealed class RestoreCompetitionEndpoint(
    RestoreCompetition restore,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<
            NoContent,
            NotFound,
            Conflict<CompetitionRestoreConflictResponse>>>
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
            Conflict<CompetitionRestoreConflictResponse>>> ExecuteAsync(
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
            CompetitionRestoreState.UserNotFound
                or CompetitionRestoreState.RoleNotEligible
                or CompetitionRestoreState.ChallengeImageInvalid
                or CompetitionRestoreState.RegistryAuthenticationRequired
                or CompetitionRestoreState.RegistryAuthenticationFailed
                or CompetitionRestoreState.RegistryUnavailable
                or CompetitionRestoreState.RegistryManifestNotFound
                or CompetitionRestoreState.RegistryManifestInvalid
                or CompetitionRestoreState.ChallengeDefinitionRevisionConflict =>
                TypedResults.Conflict(ToConflict(result)),
            _ => throw new InvalidOperationException(
                $"Unsupported competition restore state: {result.State}.")
        };
    }

    private static CompetitionRestoreConflictResponse ToConflict(
        CompetitionRestoreResult result) =>
        new(
            result.State switch
            {
                CompetitionRestoreState.UserNotFound =>
                    CompetitionRestoreConflictCodeProtocol.UserNotFound,
                CompetitionRestoreState.RoleNotEligible =>
                    CompetitionRestoreConflictCodeProtocol.RoleNotEligible,
                CompetitionRestoreState.ChallengeImageInvalid =>
                    CompetitionRestoreConflictCodeProtocol.ChallengeImageInvalid,
                CompetitionRestoreState.RegistryAuthenticationRequired =>
                    CompetitionRestoreConflictCodeProtocol.RegistryAuthenticationRequired,
                CompetitionRestoreState.RegistryAuthenticationFailed =>
                    CompetitionRestoreConflictCodeProtocol.RegistryAuthenticationFailed,
                CompetitionRestoreState.RegistryUnavailable =>
                    CompetitionRestoreConflictCodeProtocol.RegistryUnavailable,
                CompetitionRestoreState.RegistryManifestNotFound =>
                    CompetitionRestoreConflictCodeProtocol.RegistryManifestNotFound,
                CompetitionRestoreState.RegistryManifestInvalid =>
                    CompetitionRestoreConflictCodeProtocol.RegistryManifestInvalid,
                CompetitionRestoreState.ChallengeDefinitionRevisionConflict =>
                    CompetitionRestoreConflictCodeProtocol.ChallengeDefinitionRevisionConflict,
                _ => throw new InvalidOperationException(
                    $"Unsupported competition restore conflict: {result.State}.")
            },
            result.UserIds ?? [],
            result.Detail);
}
