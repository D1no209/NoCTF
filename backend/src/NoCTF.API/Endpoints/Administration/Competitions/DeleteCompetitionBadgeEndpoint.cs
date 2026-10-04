using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class DeleteCompetitionBadgeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid BadgeId { get; set; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionBadgeConflictCode>))]
public enum CompetitionBadgeConflictCode { BadgeInUse, ConcurrencyConflict }

public sealed record CompetitionBadgeConflictResponse(CompetitionBadgeConflictCode Code, string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class DeleteCompetitionBadgeEndpoint(
    ManageCompetitionBadges badges,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider clock)
    : Endpoint<DeleteCompetitionBadgeRequest,
        Results<NoContent, NotFound, ForbidHttpResult, Conflict<CompetitionBadgeConflictResponse>>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Removes a badge from the competition catalog when permitted.";
            summary.Description = summary.Summary;
        });

        Delete("/admin/competitions/{competitionId}/badges/{badgeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionBadge"));
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<CompetitionBadgeConflictResponse>>>
        ExecuteAsync(DeleteCompetitionBadgeRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var failure = await badges.DeleteAsync(
            request.CompetitionId, request.BadgeId, clock.GetUtcNow(), ct);
        return failure switch
        {
            null => TypedResults.NoContent(),
            CompetitionBadgeFailure.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Conflict(new CompetitionBadgeConflictResponse(
                CompetitionBadgeConflictCode.BadgeInUse, "Badge is referenced by the progression graph."))
        };
    }
}
