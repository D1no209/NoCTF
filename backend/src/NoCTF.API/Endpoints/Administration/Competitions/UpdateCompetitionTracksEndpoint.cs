using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions.Tracks;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record UpdateCompetitionTrackRequest(
    string Key,
    string Name,
    bool IsDefault,
    bool IsPublicSelectable,
    bool IsInternal,
    bool EarnsScore,
    bool EarnsBlood,
    bool AffectsDynamicChallengeScore,
    bool VisibleOnLeaderboard,
    bool AffectsCompetitiveResults);

public sealed record UpdateCompetitionTracksRequest(
    int ExpectedRevision,
    IReadOnlyList<UpdateCompetitionTrackRequest> Tracks);

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionTrackFailureCodeProtocol>))]
public enum CompetitionTrackFailureCodeProtocol
{
    CompetitionNotFound,
    InvalidConfiguration,
    ConfigurationLocked,
    ConfigurationConflict,
    TrackInUse,
    TeamNotFound,
    TrackNotFound,
    TrackNotPublicSelectable,
    AssignmentLocked,
    AssignmentConflict
}

public sealed record CompetitionTrackFailureResponse(
    CompetitionTrackFailureCodeProtocol Code,
    string Detail);

public sealed class UpdateCompetitionTracksValidator : Validator<UpdateCompetitionTracksRequest>
{
    public UpdateCompetitionTracksValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Tracks).NotNull().Must(tracks => tracks.Count is >= 1 and <= 32);
    }
}

internal static class CompetitionTrackFailureMapping
{
    public static CompetitionTrackFailureResponse ToResponse(
        CompetitionTrackFailureCode code,
        string? detail) => new(
        Enum.Parse<CompetitionTrackFailureCodeProtocol>(code.ToString()),
        detail ?? "The competition track operation failed.");
}

public sealed class UpdateCompetitionTracksEndpoint(
    GetCompetitionTracks get,
    UpdateCompetitionTracks update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateCompetitionTracksRequest,
        Results<
            Ok<CompetitionTrackListResponse>,
            Conflict<CompetitionTrackFailureResponse>,
            NotFound,
            ForbidHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/tracks");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCompetitionTracks_Update"));
        Summary(summary =>
        {
            summary.Summary = "Updates competition tracks before the competition starts.";
            summary.Description = "Validates the complete track definition set and applies optimistic concurrency.";
        });
    }

    public override async Task<Results<
        Ok<CompetitionTrackListResponse>,
        Conflict<CompetitionTrackFailureResponse>,
        NotFound,
        ForbidHttpResult>> ExecuteAsync(
        UpdateCompetitionTracksRequest request,
        CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, cancellationToken))
            return TypedResults.Forbid();
        var current = await get.ExecuteAsync(
            competitionId,
            user.UserId,
            includeInternal: true,
            cancellationToken);
        if (current is null)
            return TypedResults.NotFound();

        var result = await update.ExecuteAsync(new UpdateCompetitionTracksCommand(
            competitionId,
            request.ExpectedRevision,
            request.Tracks.Select(track => new CompetitionTrackDefinition(
                track.Key,
                track.Name,
                track.IsDefault,
                track.IsPublicSelectable,
                track.IsInternal,
                track.EarnsScore,
                track.EarnsBlood,
                track.AffectsDynamicChallengeScore,
                track.VisibleOnLeaderboard,
                track.AffectsCompetitiveResults)).ToArray(),
            user.UserId,
            DateTimeOffset.UtcNow), current.Mode, cancellationToken);
        if (result.FailureCode == CompetitionTrackFailureCode.CompetitionNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            var response = CompetitionTrackFailureMapping.ToResponse(
                result.FailureCode!.Value,
                result.ErrorMessage);
            return TypedResults.Conflict(response);
        }
        return TypedResults.Ok(CompetitionTrackProtocolMapping.ToResponse(result.Value!));
    }
}
