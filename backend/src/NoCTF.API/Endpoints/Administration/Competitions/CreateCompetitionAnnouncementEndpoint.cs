using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Notifications;
using NoCTF.API.Security;
using NoCTF.Application.Notifications;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AnnouncementAudience>))]
public enum AnnouncementAudience
{
    Collaborators,
    Participants
}

public sealed class CreateCompetitionAnnouncementRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.Collaborators;
}

public sealed class CreateCompetitionAnnouncementValidator
    : Validator<CreateCompetitionAnnouncementRequest>
{
    public CreateCompetitionAnnouncementValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Body).NotEmpty().MaximumLength(16_000);
        RuleFor(request => request.Audience).IsInEnum();
    }
}

public sealed class CreateCompetitionAnnouncementEndpoint(
    PublishCompetitionAnnouncement publish,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<CreateCompetitionAnnouncementRequest,
        Results<Created<NotificationResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/announcements");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateCompetitionAnnouncement"));
        Summary(summary =>
        {
            summary.Summary = "Publish one competition announcement.";
            summary.Description = "Management announcements default to collaborators; participant visibility must be explicit.";
        });
    }

    public override async Task<Results<Created<NotificationResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CreateCompetitionAnnouncementRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var item = await publish.ExecuteAsync(new(
            competitionId,
            user.UserId,
            request.Title!,
            request.Body!,
            request.Audience == AnnouncementAudience.Participants
                ? CompetitionAnnouncementAudience.Participants
                : CompetitionAnnouncementAudience.Collaborators,
            DateTimeOffset.UtcNow), ct);
        if (item is null)
            return TypedResults.NotFound();
        var response = new NotificationResponse(
            item.Id,
            item.SourceType,
            item.SourceId,
            item.TargetType,
            item.TargetId,
            NotificationProtocolMapper.ToProtocol(item.Kind),
            JsonSerializer.Deserialize<JsonElement>(item.ContentJson),
            item.RelatedType,
            item.RelatedId,
            item.ReplyToId,
            item.SentAt,
            item.SourceDisplayName);
        return TypedResults.Created($"/notifications/{item.Id}", response);
    }
}
