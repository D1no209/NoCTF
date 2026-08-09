using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using NoCTF.API.Endpoints.Submissions;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.CheatIncidents;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Submissions;

namespace NoCTF.API.Endpoints.Administration.CheatIncidents;

public sealed class GetCheatIncidentRequest
{
    public Guid CompetitionId { get; set; }
    public Guid ScoringEventId { get; set; }
}

public sealed record CheatIncidentDetailResponse(
    Guid ScoringEventId,
    Guid SubmissionId,
    string SubmittedFlag,
    Guid SourceTeamId,
    string SourceTeamName,
    Guid OwnerTeamId,
    string OwnerTeamName,
    Guid SubmittedByUserId,
    string SubmittedByUserName,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    SubmissionKindProtocol SubmissionKind,
    ScoringResultProtocol Result,
    ScoringFailureCodeProtocol FailureCode,
    CheatIncidentStatusProtocol Status,
    Guid? ResolvedByUserId,
    string? ResolvedByUserName,
    DateTimeOffset? ResolvedAt,
    string? ResolutionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset DetectedAt,
    bool SourceTeamIsBanned,
    DateTimeOffset? SourceTeamBannedAt,
    Guid? SourceTeamBannedByUserId,
    string? SourceTeamBanReason,
    bool CanDismiss,
    bool CanConfirm,
    bool CanCorrect);

public sealed class GetCheatIncidentEndpoint(
    AccessCheatIncident access,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<GetCheatIncidentRequest,
        Results<Ok<CheatIncidentDetailResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/cheat-incidents/{scoringEventId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCheatIncident"));
        Summary(summary =>
        {
            summary.Summary = "Reads one protected cross-team Flag incident.";
            summary.Description =
                "Administrator, owner, manager, and judge only. The full Flag response is never cached and every successful read is audited.";
        });
    }

    public override async Task<
        Results<Ok<CheatIncidentDetailResponse>, NotFound, ForbidHttpResult>>
        ExecuteAsync(
            GetCheatIncidentRequest request,
            CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.ScoringEventId = Route<Guid>("scoringEventId");
        if (!await authorizer.CanJudgeAsync(
                user.UserId,
                request.CompetitionId,
                cancellationToken))
        {
            return TypedResults.Forbid();
        }
        HttpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            NoStore = true,
            NoCache = true
        };
        var detail = await access.ExecuteAsync(
            request.CompetitionId,
            request.ScoringEventId,
            user.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (detail is null)
            return TypedResults.NotFound();
        var canConfirm = await authorizer.CanJudgeAsync(
            user.UserId,
            request.CompetitionId,
            cancellationToken);
        return TypedResults.Ok(new CheatIncidentDetailResponse(
            detail.ScoringEventId,
            detail.SubmissionId,
            detail.SubmittedFlag,
            detail.SourceTeamId,
            detail.SourceTeamName,
            detail.OwnerTeamId,
            detail.OwnerTeamName,
            detail.SubmittedByUserId,
            detail.SubmittedByUserName,
            detail.CompetitionChallengeId,
            detail.ChallengeTitle,
            SubmissionMapper.ToProtocol(detail.SubmissionKind),
            SubmissionMapper.ToProtocol(detail.Result),
            SubmissionMapper.ToProtocol(detail.FailureCode),
            CheatIncidentProtocolMapper.ToProtocol(detail.Status),
            detail.ResolvedByUserId,
            detail.ResolvedByUserName,
            detail.ResolvedAt,
            detail.ResolutionReason,
            detail.SubmittedAt,
            detail.DetectedAt,
            detail.SourceTeamIsBanned,
            detail.SourceTeamBannedAt,
            detail.SourceTeamBannedByUserId,
            detail.SourceTeamBanReason,
            detail.Status == CheatIncidentStatus.Pending,
            canConfirm && detail.Status == CheatIncidentStatus.Pending,
            canConfirm
                && detail.Status == CheatIncidentStatus.Confirmed
                && detail.SourceTeamIsBanned));
    }
}
