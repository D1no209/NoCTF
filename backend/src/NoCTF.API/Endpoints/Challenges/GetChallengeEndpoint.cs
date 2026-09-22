using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Challenges.Hints;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Challenges;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CtfInteractionKindProtocol>))]
public enum CtfInteractionKindProtocol
{
    FlagSubmission,
    PatchVerification
}

public sealed record ChallengeResponse(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? CustomTitle,
    string? Description,
    string Direction,
    int Order,
    bool IsPublished,
    DateTimeOffset? DeletedAt,
    bool HasRuntime,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ControlFlag = null,
    IReadOnlyList<RuntimeAccessResponse>? Accesses = null,
    LeaderboardVisibilityProtocol LeaderboardVisibility = LeaderboardVisibilityProtocol.Normal,
    LeaderboardDataScopeProtocol DataScope = LeaderboardDataScopeProtocol.Live,
    int? MaximumFlagAttempts = null,
    int? AcceptedFlagAttempts = null,
    int? RemainingFlagAttempts = null,
    bool SolvedByMyTeam = false,
    bool UsesDynamicFlag = false,
    IReadOnlyList<ParticipantChallengeHintResponse>? Hints = null,
    CtfInteractionKindProtocol InteractionKind = CtfInteractionKindProtocol.FlagSubmission,
    bool PatchVerificationAvailable = false,
    int? MaximumPatchAttempts = null,
    int? AcceptedPatchAttempts = null,
    int? RemainingPatchAttempts = null,
    GameplayFactStateProtocol? PatchVerificationState = null,
    GameplayFactResultProtocol? PatchVerificationResult = null,
    GameplayFactFailureCodeProtocol? PatchVerificationFailureCode = null,
    Guid? PatchVerificationRuntimeInstanceId = null,
    RuntimeStateProtocol? PatchVerificationRuntimeState = null);

public sealed record ParticipantChallengeHintResponse(
    Guid Id,
    long Cost,
    DateTimeOffset PublishedAt,
    string? Content,
    bool IsUnlocked,
    bool CanUnlock);

public sealed record ChallengeListResponse(
    IReadOnlyList<ChallengeResponse> Items,
    LeaderboardVisibilityProtocol LeaderboardVisibility = LeaderboardVisibilityProtocol.Normal,
    LeaderboardDataScopeProtocol DataScope = LeaderboardDataScopeProtocol.Live);

internal static class ChallengeMapper
{
    public static ChallengeResponse ToResponse(
        ChallengeView view,
        KohChallengeAccessView? koh = null,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live,
        FlagAttemptState? attemptState = null,
        IReadOnlyList<ParticipantChallengeHintView>? hints = null,
        PatchVerificationParticipantState? patchVerification = null,
        HttpRequest? request = null) =>
        new(
            view.Id,
            view.CompetitionId,
            view.ChallengeId,
            view.Title,
            view.CustomTitle,
            view.Description,
            view.Direction,
            view.Order,
            view.IsPublished,
            view.DeletedAt,
            view.HasRuntime,
            view.CreatedAt,
            view.UpdatedAt,
            koh?.ControlFlag,
            koh is not null && request is not null
                ? RuntimeAccessMapping.ToResponse(
                    koh.RuntimeInstanceId,
                    koh.AccessMode,
                    koh.AccessEndpoints,
                    request)
                : null,
            CompetitionProtocolMapper.ToProtocol(visibility),
            ScoreboardProtocolMapper.ToProtocol(dataScope),
            attemptState?.Maximum,
            attemptState?.Accepted,
            attemptState?.Remaining,
            attemptState?.Solved == true
                || patchVerification?.VerificationResult == NoCTF.Domain.Gameplay.GameplayFactResult.Correct,
            view.UsesDynamicFlag,
            hints?.Select(hint => new ParticipantChallengeHintResponse(
                hint.Id, hint.Cost, hint.PublishedAt, hint.Content, hint.IsUnlocked, hint.CanUnlock)).ToArray(),
            ToProtocol(view.InteractionKind),
            patchVerification?.Available ?? false,
            patchVerification?.MaximumAttempts,
            patchVerification?.AcceptedAttempts,
            patchVerification?.RemainingAttempts,
            patchVerification?.VerificationState is { } patchState
                ? GameplayFactMapper.ToProtocol(patchState)
                : null,
            patchVerification?.VerificationResult is { } patchResult
                ? GameplayFactMapper.ToProtocol(patchResult)
                : null,
            patchVerification?.VerificationFailureCode is { } patchFailure
                ? GameplayFactMapper.ToProtocol(patchFailure)
                : null,
            patchVerification?.RuntimeInstanceId,
            patchVerification?.RuntimeState is { } patchRuntimeState
                ? RuntimeProtocolMapper.ToProtocol(patchRuntimeState)
                : null);

    public static CtfInteractionKindProtocol ToProtocol(CtfInteractionKind kind) => kind switch
    {
        CtfInteractionKind.FlagSubmission => CtfInteractionKindProtocol.FlagSubmission,
        CtfInteractionKind.PatchVerification => CtfInteractionKindProtocol.PatchVerification,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static ChallengeListResponse ToListResponse(
        IReadOnlyList<ChallengeView> views,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live) =>
        new(
            views.Select(view => ToResponse(
                view,
                visibility: visibility,
                dataScope: dataScope)).ToArray(),
            CompetitionProtocolMapper.ToProtocol(visibility),
            ScoreboardProtocolMapper.ToProtocol(dataScope));
}

public sealed class GetChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
}

public sealed class GetChallengeEndpoint(
    GetChallenge get,
    IKohChallengeAccessReader kohAccess,
    ICompetitionChallengeAudienceAccess audienceAccess,
    ICompetitionVisibilityAccess visibilityAccess,
    GetFlagAttemptState getAttemptState,
    ReadParticipantChallengeHints getHints,
    IUserContext user,
    TimeProvider timeProvider,
    IExperimentalFeatureReader? experimentalFeatures = null,
    GetPatchVerificationState? getPatchVerificationState = null) : Endpoint<GetChallengeRequest, Results<Ok<ChallengeResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Gets a published challenge.");
    }

    public override async Task<Results<Ok<ChallengeResponse>, NotFound>> ExecuteAsync(
        GetChallengeRequest request,
        CancellationToken ct)
    {
        // The hint bodies are personalized by the viewer's team and must not enter shared caches.
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var competitionId = Route<Guid>("competitionId");
        if (!await audienceAccess.CanReadAsync(user.UserId, competitionId, ct))
            return TypedResults.NotFound();
        var visibility = await visibilityAccess.ResolveAsync(
            user.UserId,
            competitionId,
            timeProvider.GetUtcNow(),
            ct);
        if (visibility is null
            || !ParticipantChallengeVisibilityPolicy.CanView(
                visibility.CompetitionStatus))
            return TypedResults.NotFound();
        var item = await get.ExecuteAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            includeUnpublished: false,
            includeDeleted: false,
            ct);

        if (item is null)
            return TypedResults.NotFound();
        if (item.InteractionKind == CtfInteractionKind.PatchVerification
            && visibility.CompetitionStatus is not (CompetitionStatus.Running or CompetitionStatus.Paused)
            && !(await IsPatchVerificationEnabledAsync(ct)))
        {
            return TypedResults.NotFound();
        }
        var access = await kohAccess.FindAsync(
            item.CompetitionId,
            item.Id,
            user.UserId,
            ct);
        var attemptState = user.UserId == Guid.Empty
                || item.InteractionKind == CtfInteractionKind.PatchVerification
            ? null
            : await getAttemptState.ExecuteAsync(
                item.CompetitionId,
                item.Id,
                user.UserId,
                ct);
        var patchVerification = user.UserId == Guid.Empty
                || item.InteractionKind != CtfInteractionKind.PatchVerification
            ? null
            : getPatchVerificationState is null
                ? null
                : await getPatchVerificationState.ExecuteAsync(
                item.CompetitionId,
                item.Id,
                user.UserId,
                ct);
        var response = ChallengeMapper.ToResponse(
            item,
            access,
            visibility.Visibility,
            visibility.DataScope,
            attemptState,
            await getHints.ExecuteAsync(item.CompetitionId, item.Id, user.UserId, timeProvider.GetUtcNow(), ct),
            patchVerification,
            HttpContext.Request);
        return TypedResults.Ok(response);
    }

    private Task<bool> IsPatchVerificationEnabledAsync(CancellationToken ct) =>
        experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
        ?? Task.FromResult(false);
}
