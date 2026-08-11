using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NoCTF.Application.Common;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionTransitionConflictCodeProtocol>))]
public enum CompetitionTransitionConflictCodeProtocol
{
    CompetitionStartGateFailed,
    InvalidSchedule,
    InvalidLifecycleTransition,
    LifecycleConflict,
    ChallengeImageInvalid,
    RegistryAuthenticationRequired,
    RegistryAuthenticationFailed,
    RegistryUnavailable,
    RegistryManifestNotFound,
    RegistryManifestInvalid,
    ChallengeDefinitionRevisionConflict
}

public sealed record CompetitionTransitionConflictResponse(
    [property: Required, JsonRequired] CompetitionTransitionConflictCodeProtocol Code,
    [property: Required, JsonRequired] string Detail);

internal static class CompetitionTransitionConflictMapper
{
    public static CompetitionTransitionConflictResponse ToResponse(
        OperationResult<CompetitionTransitionFailureCode> result) =>
        new(
            result.FailureCode switch
            {
                CompetitionTransitionFailureCode.CompetitionStartGateFailed =>
                    CompetitionTransitionConflictCodeProtocol.CompetitionStartGateFailed,
                CompetitionTransitionFailureCode.InvalidSchedule =>
                    CompetitionTransitionConflictCodeProtocol.InvalidSchedule,
                CompetitionTransitionFailureCode.InvalidLifecycleTransition =>
                    CompetitionTransitionConflictCodeProtocol.InvalidLifecycleTransition,
                CompetitionTransitionFailureCode.LifecycleConflict =>
                    CompetitionTransitionConflictCodeProtocol.LifecycleConflict,
                CompetitionTransitionFailureCode.ChallengeImageInvalid =>
                    CompetitionTransitionConflictCodeProtocol.ChallengeImageInvalid,
                CompetitionTransitionFailureCode.RegistryAuthenticationRequired =>
                    CompetitionTransitionConflictCodeProtocol.RegistryAuthenticationRequired,
                CompetitionTransitionFailureCode.RegistryAuthenticationFailed =>
                    CompetitionTransitionConflictCodeProtocol.RegistryAuthenticationFailed,
                CompetitionTransitionFailureCode.RegistryUnavailable =>
                    CompetitionTransitionConflictCodeProtocol.RegistryUnavailable,
                CompetitionTransitionFailureCode.RegistryManifestNotFound =>
                    CompetitionTransitionConflictCodeProtocol.RegistryManifestNotFound,
                CompetitionTransitionFailureCode.RegistryManifestInvalid =>
                    CompetitionTransitionConflictCodeProtocol.RegistryManifestInvalid,
                CompetitionTransitionFailureCode.ChallengeDefinitionRevisionConflict =>
                    CompetitionTransitionConflictCodeProtocol.ChallengeDefinitionRevisionConflict,
                _ => throw new InvalidOperationException(
                    $"Unsupported competition transition conflict: {result.FailureCode}.")
            },
            result.ErrorMessage ?? "Competition transition was rejected.");
}

public sealed class PublishCompetitionEndpoint(TransitionCompetitionLifecycle transition, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, Conflict<CompetitionTransitionConflictResponse>>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/publish");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminPublishCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Publishes a competition.";
            summary.Description = "Moves a competition through the documented publication lifecycle state machine.";
        });
    }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<CompetitionTransitionConflictResponse>>> ExecuteAsync(CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, id, ct)) return TypedResults.Forbid();
        var result = await transition.ExecuteAsync(id, CompetitionStatus.Published, user.UserId, "manual_publish", ct);
        if (result.FailureCode == CompetitionTransitionFailureCode.CompetitionNotFound) return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Conflict(CompetitionTransitionConflictMapper.ToResponse(result));
        return TypedResults.NoContent();
    }
}
