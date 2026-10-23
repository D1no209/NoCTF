using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Scoring;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class GetCompetitionScorePresetsRequest { public Guid CompetitionId { get; set; } }
public sealed record CompetitionScorePresetResponse(Guid Id, string Name, ScoreCurveContract? ScoreCurve, int ChallengeCount);
public sealed record CompetitionScorePresetsResponse(IReadOnlyList<CompetitionScorePresetResponse> Items);
public sealed record CompetitionScorePresetMutationResponse(CompetitionScorePresetResponse? Preset,
    int AffectedChallengeCount, int ReplacedChallengeCount);
public sealed record CompetitionScorePresetFailureResponse(
    [property: Required, JsonRequired, JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionScorePresetFailure>))]
    CompetitionScorePresetFailure Code)
{
    public string Detail => ApiMessages.For(Code).Text;
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

internal static class CompetitionScorePresetResponses
{
    public static CompetitionScorePresetResponse Map(CompetitionScorePresetView item) =>
        new(item.Preset.Id, item.Preset.Name, item.Preset.ScoreCurve is { } curve ? Curve(curve) : null, item.ChallengeCount);
    public static ScoreCurveContract Curve(ScoreCurveValue curve) => new(curve.InitialPoints, curve.MinimumPoints,
        curve.DecayTeamCount, (ScoreDecayModeProtocol)curve.DecayMode, curve.CustomExpression);
    public static ScoreCurveValue Curve(ScoreCurveContract curve) => new() { InitialPoints = curve.InitialPoints,
        MinimumPoints = curve.MinimumPoints, DecayTeamCount = curve.DecayTeamCount,
        DecayMode = (PersistedScoreDecayMode)curve.DecayMode, CustomExpression = curve.CustomExpression };
    public static CompetitionScorePresetMutationResponse Map(CompetitionScorePresetMutationResult result) =>
        new(result.Item is null ? null : Map(result.Item), result.AffectedChallengeCount, result.ReplacedChallengeCount);
    public static ProblemHttpResult Failure(CompetitionScorePresetFailure failure) => ApiProblems.Problem(
        statusCode: failure is CompetitionScorePresetFailure.CompetitionNotFound or CompetitionScorePresetFailure.PresetNotFound
            ? 404 : failure == CompetitionScorePresetFailure.NameConflict ? 409 : 400,
        detail: ApiMessages.For(failure), extensions: new Dictionary<string, object?> { ["code"] = failure.ToString() });
}

public sealed class GetCompetitionScorePresetsEndpoint(ManageCompetitionScorePresets presets,
    ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<GetCompetitionScorePresetsRequest, Results<Ok<CompetitionScorePresetsResponse>, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/score-presets"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionScorePresets"));
        Summary(summary =>
        {
            summary.Response<CompetitionScorePresetsResponse>(200);
            summary.Response<Microsoft.AspNetCore.Mvc.ProblemDetails>(404);
        });
    }
    public override async Task<Results<Ok<CompetitionScorePresetsResponse>, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        GetCompetitionScorePresetsRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await presets.ListAsync(request.CompetitionId, ct);
        return result.Failure is { } failure ? CompetitionScorePresetResponses.Failure(failure)
            : TypedResults.Ok(new CompetitionScorePresetsResponse(result.Items!.Select(CompetitionScorePresetResponses.Map).ToArray()));
    }
}
