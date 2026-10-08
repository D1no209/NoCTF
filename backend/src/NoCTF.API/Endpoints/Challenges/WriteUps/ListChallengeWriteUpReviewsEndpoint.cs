using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeWriteUpReviewFilterProtocol>))]
public enum ChallengeWriteUpReviewFilterProtocol { All, Submitted, Published, Rejected, Draft }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeWriteUpReviewSourceProtocol>))]
public enum ChallengeWriteUpReviewSourceProtocol { Team, Official }

public sealed class ListChallengeWriteUpReviewsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public string? Search { get; set; }
    public ChallengeWriteUpReviewSourceProtocol? Source { get; set; }
    public ChallengeWriteUpReviewFilterProtocol Filter { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 25;
}
public sealed class ListChallengeWriteUpReviewsValidator : Validator<ListChallengeWriteUpReviewsRequest>
{
    public ListChallengeWriteUpReviewsValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.Offset).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100); RuleFor(x => x.Search).MaximumLength(160); RuleFor(x => x.Filter).IsInEnum();
        RuleFor(x => x.Source).IsInEnum().When(x => x.Source is not null);
    }
}
public sealed record ChallengeWriteUpReviewPageResponse(IReadOnlyList<ChallengeWriteUpResponse> Items,
    int TotalCount, bool CanManage, bool CanJudge);
public sealed class ListChallengeWriteUpReviewsEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<ListChallengeWriteUpReviewsRequest, Results<Ok<ChallengeWriteUpReviewPageResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenge-writeups"); AuthSchemes("Bearer");
        Description(x => x.WithName("AdminListChallengeWriteUpReviews").WithTags("Competition WriteUps")
            .WithDescription("Observers can read metadata; judges can review; administrators, owners and managers can publish. Document bodies use separate authorized endpoints."));
        Summary(x => x.Summary = "Pages private challenge WriteUps for observers and reviewers without returning document bodies.");
    }
    public override async Task<Results<Ok<ChallengeWriteUpReviewPageResponse>, ForbidHttpResult>> ExecuteAsync(ListChallengeWriteUpReviewsRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await writeUps.ListReviewAsync(new(request.CompetitionId, user.UserId, request.CompetitionChallengeId,
            request.Search, request.Filter switch { ChallengeWriteUpReviewFilterProtocol.Submitted => WriteUpReviewFilter.Submitted,
                ChallengeWriteUpReviewFilterProtocol.Published => WriteUpReviewFilter.Published,
                ChallengeWriteUpReviewFilterProtocol.Rejected => WriteUpReviewFilter.Rejected,
                ChallengeWriteUpReviewFilterProtocol.Draft => WriteUpReviewFilter.Draft, _ => WriteUpReviewFilter.All },
            request.Offset, request.Limit, clock.GetUtcNow(), request.Source switch { ChallengeWriteUpReviewSourceProtocol.Team => WriteUpSource.Team,
                ChallengeWriteUpReviewSourceProtocol.Official => WriteUpSource.Official, _ => null }), ct);
        return result is null ? TypedResults.Forbid() : TypedResults.Ok(new ChallengeWriteUpReviewPageResponse(
            result.Items.Select(ChallengeWriteUpProtocol.Map).ToArray(), result.TotalCount, result.CanManage, result.CanJudge));
    }
}
