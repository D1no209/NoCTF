using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Teams.Moderation;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<StartGateFailureCodeProtocol>))]
public enum StartGateFailureCodeProtocol
{
    CompetitionNotPublished,
    CompetitionConfigurationInvalid,
    PublishedChallengeRequired,
    ApprovedTeamRequired,
    RuntimeQuotaInsufficient,
    ChallengeModeMismatch,
    ChallengeRulesInvalid,
    RuntimeDefinitionInvalid,
    TrackConfigurationInvalid,
    TeamTrackInvalid,
    ExperimentalFeatureDisabled
}

public sealed record StartGateErrorResponse(
    StartGateFailureCodeProtocol Code,
    Guid? CompetitionChallengeId,
    string Message)
{
    public string Message { get; init; } = ApiMessages.Localize(Code, Message);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed record StartValidationResponse(IReadOnlyList<StartGateErrorResponse> Errors);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class ValidateCompetitionStartMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial StartGateFailureCodeProtocol ToProtocol(StartGateFailureCode value);

    public static partial StartGateErrorResponse ToResponse(StartGateError value);
}

public sealed class ValidateCompetitionStartEndpoint(
    CompetitionStartGate gate,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Ok<StartValidationResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/start-validation");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminValidateCompetitionStart"));
        Summary(summary =>
        {
            summary.Summary = "Validates the competition start gate.";
            summary.Description = "Returns every current stable, deduplicated start error without changing state.";
        });
    }

    public override async Task<
        Results<Ok<StartValidationResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var errors = await gate.ValidateAsync(competitionId, ct);
        return errors is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new StartValidationResponse(
                errors.Select(ValidateCompetitionStartMapper.ToResponse).ToArray()));
    }
}
