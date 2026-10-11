using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class GetTheoryExamLeaderboardRequest : NoCTF.API.Pagination.PaginationRequest { public Guid ActivityId { get; set; } }
public sealed class GetTheoryExamLeaderboardValidator : Validator<GetTheoryExamLeaderboardRequest>
{ public GetTheoryExamLeaderboardValidator() { NoCTF.API.Pagination.PaginationRules.Add(this); RuleFor(request => request.ActivityId).NotEmpty(); } }
public sealed class GetTheoryExamLeaderboardEndpoint(ITheoryReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<GetTheoryExamLeaderboardRequest, Results<Ok<TheoryExamLeaderboard>, NotFound>>
{
    public override void Configure()
    {
        Get("/theory/activities/{activityId}/leaderboard"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        Description(builder => builder.WithName("GetTheoryExamLeaderboard"));
    }
    public override async Task<Results<Ok<TheoryExamLeaderboard>, NotFound>> ExecuteAsync(GetTheoryExamLeaderboardRequest request, CancellationToken ct)
        => await store.LeaderboardAsync(user.UserId, request.ActivityId, request.Offset, request.Limit, clock.GetUtcNow(), ct) is { } result
            ? TypedResults.Ok(result) : TypedResults.NotFound();
}
