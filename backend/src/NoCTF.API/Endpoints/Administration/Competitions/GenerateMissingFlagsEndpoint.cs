using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

 [JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<MissingFlagFailureCodeProtocol>))]
public enum MissingFlagFailureCodeProtocol
{
    CompetitionNotFound,
    GameModeUnsupported,
    FlagGenerationFailed
}

public sealed record MissingFlagGenerationFailureResponse(
    Guid CompetitionChallengeId,
    Guid TeamId,
    MissingFlagFailureCodeProtocol Code,
    string Description)
{
    public string Description { get; init; } = ApiMessages.Localize(Code, Description);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed record GenerateMissingFlagsResponse(
    IReadOnlyList<MissingFlagGenerationFailureResponse> Failures);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class GenerateMissingFlagsMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial MissingFlagFailureCodeProtocol ToProtocol(MissingFlagFailureCode value);

    public static partial MissingFlagGenerationFailureResponse ToResponse(MissingFlagGenerationFailure value);
}

public sealed class GenerateMissingFlagsEndpoint(
    GenerateMissingFlags generate,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Ok<GenerateMissingFlagsResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/flags/generate-missing");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGenerateMissingFlags"));
        Summary(summary =>
        {
            summary.Summary = "Synchronously generates missing CTF PerTeam and KoH flags.";
            summary.Description = "The response contains only stable failure metadata and never exposes flags or counts.";
        });
    }

    public override async Task<Results<Ok<GenerateMissingFlagsResponse>, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var failures = await generate.ExecuteAsync(
            competitionId, timeProvider.GetUtcNow(), ct);
        return TypedResults.Ok(new GenerateMissingFlagsResponse(
            failures.Select(GenerateMissingFlagsMapper.ToResponse).ToArray()));
    }
}
