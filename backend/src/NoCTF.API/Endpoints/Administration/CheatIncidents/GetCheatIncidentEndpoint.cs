using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.CheatIncidents;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Gameplay;

namespace NoCTF.API.Endpoints.Administration.CheatIncidents;

public sealed class GetCheatIncidentRequest
{
    public Guid CompetitionId { get; set; }
    public Guid GameplayFactId { get; set; }
}

public sealed record CheatIncidentDetailResponse(
    Guid GameplayFactId,
    string Value,
    Guid SourceTeamId,
    string SourceTeamName,
    Guid? OwnerTeamId,
    string? OwnerTeamName,
    Guid ActorUserId,
    string SubmittedByUserName,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    GameplayFactKindProtocol GameplayFactKind,
    GameplayFactResultProtocol Result,
    GameplayFactFailureCodeProtocol FailureCode,
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
    bool CanCorrect)
{
    public FlagAcquisitionEvidenceResponse? AcquisitionEvidence { get; init; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<FlagAcquisitionEvidenceSourceProtocol>))]
public enum FlagAcquisitionEvidenceSourceProtocol { Recorded, LegacySubmission }

public sealed record FlagAcquisitionEvidenceResponse(
    bool Applicable, bool RequiresContainer, bool RequiresAttachment, bool ContainerAcquired, bool AttachmentAcquired,
    FlagAcquisitionEvidenceSourceProtocol Source, DateTimeOffset? CapturedAt,
    Guid? RuntimeInstanceId, DateTimeOffset? RuntimeStartedAt,
    Guid? AttachmentDownloadFactId, DateTimeOffset? AttachmentDownloadedAt);

internal static class FlagAcquisitionEvidenceMapper
{
    public static FlagAcquisitionEvidenceResponse ToResponse(FlagAcquisitionEvidence? value) => value is null
        ? new(false, false, false, true, true, FlagAcquisitionEvidenceSourceProtocol.LegacySubmission, null, null, null, null, null)
        : new(value.Scope is FlagAcquisitionScope.FormalStaticCtf or FlagAcquisitionScope.FormalStaticExecution,
            value.Required.HasFlag(FlagAcquisitionResource.Container), value.Required.HasFlag(FlagAcquisitionResource.Attachment),
            value.Acquired.HasFlag(FlagAcquisitionResource.Container), value.Acquired.HasFlag(FlagAcquisitionResource.Attachment),
            value.Source switch
            {
                FlagAcquisitionEvidenceSource.Recorded => FlagAcquisitionEvidenceSourceProtocol.Recorded,
                FlagAcquisitionEvidenceSource.LegacySubmission => FlagAcquisitionEvidenceSourceProtocol.LegacySubmission,
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            }, value.CapturedAt, value.RuntimeInstanceId, value.RuntimeStartedAt,
            value.AttachmentDownloadFactId, value.AttachmentDownloadedAt);
}

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
        Get("/admin/competitions/{competitionId}/cheat-incidents/{gameplayFactId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCheatIncident"));
        Summary(summary =>
        {
            summary.Summary = "Reads one protected Flag cheat incident.";
            summary.Description =
                "Administrator, owner, manager, and judge only. The full Flag response is never cached. Owner, manager, and judge reads are audited; platform Administrator reads are not.";
        });
    }

    public override async Task<
        Results<Ok<CheatIncidentDetailResponse>, NotFound, ForbidHttpResult>>
        ExecuteAsync(
            GetCheatIncidentRequest request,
            CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.GameplayFactId = Route<Guid>("gameplayFactId");
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
            request.GameplayFactId,
            user.UserId,
            user.IsAdministrator,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (detail is null)
            return TypedResults.NotFound();
        var canConfirm = await authorizer.CanJudgeAsync(
            user.UserId,
            request.CompetitionId,
            cancellationToken);
        return TypedResults.Ok(new CheatIncidentDetailResponse(
            detail.GameplayFactId,
            detail.Value,
            detail.SourceTeamId,
            detail.SourceTeamName,
            detail.OwnerTeamId,
            detail.OwnerTeamName,
            detail.ActorUserId,
            detail.SubmittedByUserName,
            detail.CompetitionChallengeId,
            detail.ChallengeTitle,
            GameplayFactMapper.ToProtocol(detail.GameplayFactKind),
            GameplayFactMapper.ToProtocol(detail.Result),
            GameplayFactMapper.ToProtocol(detail.FailureCode),
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
                && detail.SourceTeamIsBanned)
        {
            AcquisitionEvidence = FlagAcquisitionEvidenceMapper.ToResponse(detail.AcquisitionEvidence)
        });
    }
}
