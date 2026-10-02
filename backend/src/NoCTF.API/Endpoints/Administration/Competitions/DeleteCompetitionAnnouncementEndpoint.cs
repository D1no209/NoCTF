using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Notifications;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Notifications;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class DeleteCompetitionAnnouncementRequest { public Guid AnnouncementId { get; set; } }
public sealed class DeleteCompetitionAnnouncementValidator : Validator<DeleteCompetitionAnnouncementRequest>
{
    public DeleteCompetitionAnnouncementValidator() => RuleFor(request => request.AnnouncementId).NotEmpty();
}
public sealed class DeleteCompetitionAnnouncementEndpoint(ManageCompetitionAnnouncements announcements,
    ICompetitionModerationAuthorizer authorizer, IUserContext user, TimeProvider clock)
    : Endpoint<DeleteCompetitionAnnouncementRequest, Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/announcements/{announcementId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionAnnouncement"));
        Summary(summary =>
        {
            summary.Summary = "Withdraws a competition announcement.";
            summary.Description = "Authorized judges can withdraw an announcement from inbox and public readers while retaining its immutable history. Repeated withdrawals are idempotent.";
        });
    }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(DeleteCompetitionAnnouncementRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await announcements.ChangeAsync(new(competitionId, request.AnnouncementId, user.UserId,
            CompetitionAnnouncementChangeAction.Withdraw, null, null, clock.GetUtcNow()), ct);
        if (result.Failure == CompetitionAnnouncementFailure.NotFound) return TypedResults.NotFound();
        return result.Announcement is null ? AnnouncementMutationProblems.Failure(result.Failure) : TypedResults.NoContent();
    }
}
