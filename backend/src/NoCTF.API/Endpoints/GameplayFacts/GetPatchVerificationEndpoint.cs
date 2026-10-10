using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Progression;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed class GetPatchVerificationRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
}

public sealed record PatchVerificationStateResponse(
    bool PatchVerificationAvailable,
    int MaximumAttempts,
    int AcceptedAttempts,
    int RemainingAttempts,
    GameplayFactStateProtocol? VerificationState,
    GameplayFactResultProtocol? VerificationResult,
    GameplayFactFailureCodeProtocol? VerificationFailureCode,
    Guid? RuntimeInstanceId,
    NoCTF.API.Endpoints.Runtime.RuntimeStateProtocol? RuntimeState,
    PatchVerificationTargetFailureCodeProtocol? UnavailableCode,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<NoCTF.Domain.Challenges.GameplayFactTimeEligibility>))]
    NoCTF.Domain.Challenges.GameplayFactTimeEligibility TimeEligibility = NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid);

public sealed class GetPatchVerificationEndpoint(
    GetPatchVerificationState getState,
    ICompetitionChallengeReadAccess readAccess,
    IProgressionChallengeAccess progressionAccess,
    TimeProvider clock,
    IUserContext user)
    : Endpoint<GetPatchVerificationRequest,
        Results<Ok<PatchVerificationStateResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-verification");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Gets the participant PatchVerification state.");
    }

    public override async Task<Results<Ok<PatchVerificationStateResponse>, NotFound>> ExecuteAsync(
        GetPatchVerificationRequest request,
        CancellationToken ct)
    {
        var decision = await readAccess.ResolveAsync(
            user.UserId, request.CompetitionId, clock.GetUtcNow(), ct);
        if (decision is null
            || !await progressionAccess.IsActiveAsync(
                request.CompetitionId, request.CompetitionChallengeId,
                decision.TeamId, ct))
            return TypedResults.NotFound();
        var state = await getState.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            ct);
        if (state is null)
            return TypedResults.NotFound();
        return TypedResults.Ok(new PatchVerificationStateResponse(
            state.Available,
            state.MaximumAttempts,
            state.AcceptedAttempts,
            state.RemainingAttempts,
            state.VerificationState is { } factState
                ? GameplayFactMapper.ToProtocol(factState)
                : null,
            state.VerificationResult is { } factResult
                ? GameplayFactMapper.ToProtocol(factResult)
                : null,
            state.VerificationFailureCode is { } failureCode
                ? GameplayFactMapper.ToProtocol(failureCode)
                : null,
            state.RuntimeInstanceId,
            state.RuntimeState is { } runtimeState
                ? NoCTF.API.Endpoints.Runtime.RuntimeProtocolMapper.ToProtocol(runtimeState)
                : null,
            state.UnavailableCode is { } unavailableCode
                ? PatchVerificationTargetProtocolMapping.ToProtocol(unavailableCode)
                : null, state.TimeEligibility));
    }
}
