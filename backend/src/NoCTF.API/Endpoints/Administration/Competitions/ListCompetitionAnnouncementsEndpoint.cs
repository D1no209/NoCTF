using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Notifications;
using NoCTF.Application.Teams.Moderation;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class ListCompetitionAnnouncementsRequest : PaginationRequest
{
    [QueryParam] public bool IncludeWithdrawn { get; set; }
}
public sealed class ListCompetitionAnnouncementsValidator : Validator<ListCompetitionAnnouncementsRequest>
{
    public ListCompetitionAnnouncementsValidator() => PaginationRules.Add(this);
}
[JsonConverter(typeof(StrictPascalCaseEnumConverter<ManagedAnnouncementStateProtocol>))]
public enum ManagedAnnouncementStateProtocol { Published, Withdrawn }
public sealed record ManagedAnnouncementResponse(Guid Id, string Title, string Body, AnnouncementAudience Audience,
    ManagedAnnouncementStateProtocol State, Guid? AuthorId, string? AuthorName, DateTimeOffset PublishedAt, DateTimeOffset UpdatedAt);
public sealed record ManagedAnnouncementListResponse(IReadOnlyList<ManagedAnnouncementResponse> Items, int Total);

internal static class ManagedAnnouncementMapping
{
    public static ManagedAnnouncementResponse ToResponse(ManagedCompetitionAnnouncement item) => new(item.Id, item.Title, item.Body,
        item.Audience == CompetitionAnnouncementAudience.Participants ? AnnouncementAudience.Participants : AnnouncementAudience.Collaborators,
        item.State == CompetitionAnnouncementState.Withdrawn ? ManagedAnnouncementStateProtocol.Withdrawn : ManagedAnnouncementStateProtocol.Published,
        item.AuthorId, item.AuthorName, item.PublishedAt, item.UpdatedAt);
}

public sealed class ListCompetitionAnnouncementsEndpoint(ManageCompetitionAnnouncements announcements,
    ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<ListCompetitionAnnouncementsRequest, Results<Ok<ManagedAnnouncementListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/announcements");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionAnnouncements"));
        Summary(summary =>
        {
            summary.Summary = "Lists competition announcements for management.";
            summary.Description = "Returns current announcement content and withdrawal state to authorized observers with offset pagination.";
        });
    }
    public override async Task<Results<Ok<ManagedAnnouncementListResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(ListCompetitionAnnouncementsRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var page = await announcements.ListAsync(competitionId, request.IncludeWithdrawn, request.Offset, request.Limit, request.Desc, ct);
        return page is null ? TypedResults.NotFound() : TypedResults.Ok(new ManagedAnnouncementListResponse(page.Items.Select(ManagedAnnouncementMapping.ToResponse).ToArray(), page.Total));
    }
}
