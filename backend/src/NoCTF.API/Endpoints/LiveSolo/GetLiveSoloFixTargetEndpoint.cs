using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Domain.Runtime;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloFixTargetRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
}
public sealed class GetLiveSoloFixTargetValidator : Validator<GetLiveSoloFixTargetRequest>
{
    public GetLiveSoloFixTargetValidator()
    { RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.MatchId).NotEmpty(); RuleFor(value => value.RoundId).NotEmpty(); RuleFor(value => value.QuestionId).NotEmpty(); }
}
public sealed record LiveSoloFixTargetResponse(Guid? RuntimeInstanceId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeState>))] RuntimeState? State,
    Guid? GameplayFactId, string StatusUrl, long? AdmissionSequence = null);

public sealed class GetLiveSoloFixTargetEndpoint(ManageLiveSoloFixTargets targets, IUserContext user, TimeProvider clock)
    : Endpoint<GetLiveSoloFixTargetRequest, Results<Ok<LiveSoloFixTargetResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/fix-target");
        AuthSchemes("Bearer"); Description(builder => builder.WithName("GetLiveSoloFixTarget"));
    }
    public override async Task<Results<Ok<LiveSoloFixTargetResponse>, NotFound>> ExecuteAsync(GetLiveSoloFixTargetRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var target = await targets.ReadAsync(new(request.CompetitionId, request.MatchId, request.RoundId, request.QuestionId, user.UserId, clock.GetUtcNow()), ct);
        return target.Failure is not null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloFixTargetResponse(target.RuntimeInstanceId,
            target.State, target.GameplayFactId, $"/api/v1/competitions/{request.CompetitionId}/live-solo/matches/{request.MatchId}/rounds/{request.RoundId}/questions/{request.QuestionId}/fix-target", target.AdmissionSequence));
    }
}
