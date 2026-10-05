using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Challenges.Management;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Competitions.Progression;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class ListChallengesRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class ListChallengesEndpoint(
    ListChallenges list,
    ICompetitionChallengeReadAccess readAccess,
    IProgressionChallengeAccess progressionAccess,
    IUserContext user,
    TimeProvider timeProvider,
    IExperimentalFeatureReader? experimentalFeatures = null) : Endpoint<ListChallengesRequest, Results<Ok<ChallengeListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Lists published challenges for a competition.");
    }

    public override async Task<Results<Ok<ChallengeListResponse>, NotFound>> ExecuteAsync(
        ListChallengesRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var decision = await readAccess.ResolveAsync(
            user.UserId,
            competitionId,
            timeProvider.GetUtcNow(),
            ct);
        if (decision is null
            || !ParticipantChallengeVisibilityPolicy.CanView(
                decision.Visibility.CompetitionStatus))
            return TypedResults.NotFound();
        var visibility = decision.Visibility;
        var items = await list.ExecuteAsync(
            competitionId,
            includeUnpublished: false,
            includeDeleted: false,
            ct);
        if (visibility.CompetitionStatus is not (CompetitionStatus.Running or CompetitionStatus.Paused)
            && !(await IsPatchVerificationEnabledAsync(ct)))
        {
            items = items.Where(item =>
                item.InteractionKind != CtfInteractionKind.PatchVerification).ToArray();
        }
        var response = ChallengeMapper.ToListResponse(
            items, visibility.Visibility, visibility.DataScope);
        if (visibility.GameMode != GameMode.Ctf)
            return TypedResults.Ok(response);
        var statuses = await progressionAccess.ReadStatusesAsync(
            competitionId, decision.TeamId, ct);
        return TypedResults.Ok(response with
        {
            Items = response.Items.Select(item =>
            {
                if (!statuses.TryGetValue(item.Id, out var status)) return item;
                return item with
                {
                    Locked = !status.Active,
                    PrerequisitesSatisfied = status.PrerequisitesSatisfied,
                    PrerequisitesTotal = status.PrerequisitesTotal
                };
            }).ToArray()
        });
    }

    private Task<bool> IsPatchVerificationEnabledAsync(CancellationToken ct) =>
        experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
        ?? Task.FromResult(false);
}
