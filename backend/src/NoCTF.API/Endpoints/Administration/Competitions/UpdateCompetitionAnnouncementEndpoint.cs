using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Notifications;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Notifications;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionAnnouncementRequest
{
    public Guid AnnouncementId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
public sealed class UpdateCompetitionAnnouncementValidator : Validator<UpdateCompetitionAnnouncementRequest>
{
    public UpdateCompetitionAnnouncementValidator()
    {
        RuleFor(request => request.AnnouncementId).NotEmpty();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Body).NotEmpty().MaximumLength(16_000);
    }
}
internal static class AnnouncementMutationProblems
{
    public static ProblemHttpResult Failure(CompetitionAnnouncementFailure? failure) => ApiProblems.Problem(
        statusCode: failure == CompetitionAnnouncementFailure.InvalidContent ? 400 : 409,
        title: ApiMessages.For(failure),
        extensions: new Dictionary<string, object?> { ["code"] = failure?.ToString() });
}
public sealed class UpdateCompetitionAnnouncementEndpoint(ManageCompetitionAnnouncements announcements,
    ICompetitionModerationAuthorizer authorizer, IUserContext user, TimeProvider clock)
    : Endpoint<UpdateCompetitionAnnouncementRequest, Results<Ok<ManagedAnnouncementResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/competitions/{competitionId}/announcements/{announcementId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionAnnouncement"));
        Summary(summary =>
        {
            summary.Summary = "Edits a published competition announcement.";
            summary.Description = "Authorized judges can update title and body. The original audience and history are retained; inbox and public readers receive current content.";
        });
    }
    public override async Task<Results<Ok<ManagedAnnouncementResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(UpdateCompetitionAnnouncementRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await announcements.ChangeAsync(new(competitionId, request.AnnouncementId, user.UserId,
            CompetitionAnnouncementChangeAction.Edit, request.Title, request.Body, clock.GetUtcNow()), ct);
        if (result.Failure == CompetitionAnnouncementFailure.NotFound) return TypedResults.NotFound();
        return result.Announcement is null ? AnnouncementMutationProblems.Failure(result.Failure) : TypedResults.Ok(ManagedAnnouncementMapping.ToResponse(result.Announcement));
    }
}
