using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed record ChallengeFlagResponse(
    Guid Id,
    Guid? ChallengeId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    string Flag,
    SpecificationKind? SpecificationKind,
    Guid? SpecificationId,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidUntil,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt);

public sealed record ChallengeFlagListResponse(IReadOnlyList<ChallengeFlagResponse> Items);

internal static class ChallengeFlagMapping
{
    public static ChallengeFlagResponse ToResponse(ChallengeFlagView view) =>
        new(
            view.Id, view.ChallengeId, view.CompetitionChallengeId, view.TeamId,
            view.Flag, view.SpecificationKind, view.SpecificationId,
            view.ValidStart, view.ValidUntil, view.DeletedAt, view.CreatedAt);
}

public sealed class ListChallengeFlagsRequest
{
    public Guid ChallengeId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class ListChallengeFlagsEndpoint(
    ManageChallengeFlags flags,
    IUserContext user)
    : Endpoint<ListChallengeFlagsRequest, Results<Ok<ChallengeFlagListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/challenges/{challengeId}/flags");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankListFlags"));
        Summary(summary =>
        {
            summary.Summary = "Lists template-level static flags.";
            summary.Description = "Returns protected static flag records to authorized template managers.";
        });
    }

    public override async Task<Results<Ok<ChallengeFlagListResponse>, NotFound>> ExecuteAsync(
        ListChallengeFlagsRequest request,
        CancellationToken ct)
    {
        var items = await flags.ListAsync(
            ChallengeFlagScope.Template(request.ChallengeId),
            user.UserId,
            user.IsAdministrator,
            request.IncludeDeleted,
            ct);
        return items is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ChallengeFlagListResponse(
                items.Select(ChallengeFlagMapping.ToResponse).ToArray()));
    }
}
