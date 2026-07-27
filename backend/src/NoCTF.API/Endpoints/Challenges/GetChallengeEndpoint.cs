using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Challenges.Management;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Challenges;

public sealed record ChallengeResponse(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? Description,
    string Direction,
    long BaseScore,
    int Order,
    bool IsPublished,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ChallengeListResponse(IReadOnlyList<ChallengeResponse> Items);

[Mapper(
    RequiredMappingStrategy = RequiredMappingStrategy.Source,
    EnumMappingStrategy = EnumMappingStrategy.ByName)]
internal static partial class ChallengeMapper
{
    public static partial ChallengeResponse ToResponse(ChallengeView view);
    private static partial IReadOnlyList<ChallengeResponse> ToResponses(
        IReadOnlyList<ChallengeView> views);

    public static ChallengeListResponse ToListResponse(IReadOnlyList<ChallengeView> views) =>
        new(ToResponses(views));
}

public sealed class GetChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
}

public sealed class GetChallengeEndpoint(GetChallenge get) : Endpoint<GetChallengeRequest, Results<Ok<ChallengeResponse>, NotFound>>
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
        var item = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            includeUnpublished: false,
            ct);

        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeMapper.ToResponse(item));
    }
}
