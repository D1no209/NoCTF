using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.Competitions.Tracks;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Common;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Authentication.Sso;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.Domain.Competitions;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionTrackRequest
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required bool IsDefault { get; set; }
    public required bool IsPublicSelectable { get; set; }
    public required bool IsInternal { get; set; }
    public required bool EarnsScore { get; set; }
    public required bool EarnsBlood { get; set; }
    public required bool AffectsDynamicChallengeScore { get; set; }
    public required bool VisibleOnLeaderboard { get; set; }
    public required bool AffectsCompetitiveResults { get; set; }
    public required Guid? RequiredSsoProviderId { get; set; }
    public required string? InvitationCode { get; set; }
    public required bool ClearInvitationCode { get; set; }
}

public sealed class CompetitionMetadataPatchRequest
{
    public required string Title { get; set; }
    public required string? Description { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    public required bool TeamRegistrationAutoApprove { get; set; }
    public required bool AllowTeamRegistrationWhileRunning { get; set; }
    public required int MaxTeamMembers { get; set; }
    public required int MaxConcurrentRuntimeInstancesPerTeam { get; set; }
    public required int MaxActiveQuestionsPerTeam { get; set; }
    public required int MaxParticipantMessagesBeforeHandlerReply { get; set; }
    public required bool AllowChallengeOwnersToHandleQuestions { get; set; }
    public required bool PracticeModeEnabled { get; set; }
    public required bool WriteUpSubmissionRequired { get; set; }
    public required int WriteUpSubmissionDeadlineHours { get; set; }
    public required CompetitionAccessModeProtocol AccessMode { get; set; }
    public RuntimeAccessModeProtocol RuntimeAccessMode { get; set; } = RuntimeAccessModeProtocol.Direct;
    public bool TrafficCaptureEnabled { get; set; }
    public long? TrafficCaptureLimitBytes { get; set; }
}

public sealed class CompetitionConfigurationPatchRequest
{
    public required string Json { get; set; }
}

public sealed class CompetitionTracksPatchRequest
{
    public required bool Enabled { get; set; }
    public required IReadOnlyList<UpdateCompetitionTrackRequest> Tracks { get; set; }
    public IReadOnlyList<RemovedTrackReassignmentRequest> RemovedTrackReassignments { get; set; } = [];
    [JsonIgnore] public string? TrackConfigurationJson { get; set; }
}

public sealed class RemovedTrackReassignmentRequest
{
    public required string FromTrackKey { get; set; }
    public required string ToTrackKey { get; set; }
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<
    CompetitionTrackFailureCodeProtocol>))]
public enum CompetitionTrackFailureCodeProtocol
{
    CompetitionNotFound,
    InvalidConfiguration,
    CompetitionFinished,
    TracksDisabled,
    TrackReassignmentRequired,
    InvalidTrackReassignment,
    TeamNotFound,
    TrackNotFound,
    TrackNotPublicSelectable,
    TrackSsoIdentityRequired,
    SsoProviderNotFound
}

public sealed record CompetitionTrackFailureResponse(
    CompetitionTrackFailureCodeProtocol Code,
    string Message,
    int AffectedTeamCount);

[Mapper]
internal static partial class CompetitionTrackFailureProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CompetitionTrackFailureCodeProtocol ToProtocol(
        CompetitionTrackFailureCode value);
}

public sealed class CompetitionPermissionsPatchRequest
{
    public required Guid OwnerId { get; set; }
    public required IReadOnlyList<Guid> ManagerIds { get; set; }
    public required IReadOnlyList<Guid> JudgeIds { get; set; }
    public required IReadOnlyList<Guid> ObserverIds { get; set; }
}

public sealed class CompetitionLeaderboardPatchRequest
{
    public required DateTimeOffset? FrozenStartAt { get; set; }
    public required DateTimeOffset? HiddenStartAt { get; set; }
    public string? Reason { get; set; }
}

public sealed class PatchCompetitionRequest
{
    public CompetitionMetadataPatchRequest? Metadata { get; set; }
    public CompetitionConfigurationPatchRequest? ModeConfiguration { get; set; }
    public CompetitionTracksPatchRequest? Tracks { get; set; }
    public CompetitionPermissionsPatchRequest? Permissions { get; set; }
    public CompetitionLeaderboardPatchRequest? LeaderboardVisibility { get; set; }
}

[Flags]
internal enum CompetitionPatchSection
{
    None = 0,
    Metadata = 1 << 0,
    ModeConfiguration = 1 << 1,
    Tracks = 1 << 2,
    Permissions = 1 << 3,
    LeaderboardVisibility = 1 << 4
}

public sealed class PatchCompetitionValidator : Validator<PatchCompetitionRequest>
{
    public PatchCompetitionValidator()
    {
        RuleFor(request => request).Must(request => request.Metadata is not null
            || request.ModeConfiguration is not null
            || request.Tracks is not null
            || request.Permissions is not null
            || request.LeaderboardVisibility is not null)
            .WithMessage("At least one competition section is required.");
        RuleFor(request => request.Metadata!.Title).NotEmpty().MaximumLength(160)
            .When(request => request.Metadata is not null);
        RuleFor(request => request.Metadata!.MaxTeamMembers).GreaterThan(0)
            .When(request => request.Metadata is not null);
        RuleFor(request => request.Metadata!.MaxConcurrentRuntimeInstancesPerTeam)
            .GreaterThanOrEqualTo(0)
            .When(request => request.Metadata is not null);
        RuleFor(request => request.Metadata!.MaxActiveQuestionsPerTeam).GreaterThan(0)
            .When(request => request.Metadata is not null);
        RuleFor(request => request.Metadata!.MaxParticipantMessagesBeforeHandlerReply).GreaterThan(0)
            .When(request => request.Metadata is not null);
        RuleFor(request => request.Metadata!.WriteUpSubmissionDeadlineHours)
            .InclusiveBetween(0, CompetitionWriteUpPolicy.MaximumDeadlineHours)
            .When(request => request.Metadata is not null);
        RuleFor(request => request.Metadata!.AccessMode).IsInEnum()
            .When(request => request.Metadata is not null);
        RuleFor(request => request.Metadata!.RuntimeAccessMode).IsInEnum()
            .When(request => request.Metadata is not null);
        RuleFor(request => request.Metadata!.TrafficCaptureLimitBytes)
            .InclusiveBetween(
                RuntimeAccessPolicy.MinimumCaptureLimitBytes,
                RuntimeAccessPolicy.MaximumCaptureLimitBytes)
            .When(request => request.Metadata?.TrafficCaptureLimitBytes is not null);
        RuleFor(request => request.Metadata)
            .Must(metadata => metadata is null
                || !metadata.TrafficCaptureEnabled
                || metadata.RuntimeAccessMode is RuntimeAccessModeProtocol.DirectAndWsrx
                    or RuntimeAccessModeProtocol.WsrxOnly)
            .WithMessage("Traffic capture requires a WSRX-enabled Runtime access mode.");
        RuleFor(request => request.ModeConfiguration!.Json).NotEmpty()
            .When(request => request.ModeConfiguration is not null);
        RuleFor(request => request.Tracks!.Tracks).NotNull()
            .Must(tracks => tracks.Count is >= 1 and <= 32)
            .When(request => request.Tracks is not null);
        RuleForEach(request => request.Tracks!.RemovedTrackReassignments)
            .ChildRules(reassignment =>
            {
                reassignment.RuleFor(value => value.FromTrackKey).NotEmpty().MaximumLength(64);
                reassignment.RuleFor(value => value.ToTrackKey).NotEmpty().MaximumLength(64);
            })
            .When(request => request.Tracks is not null);
        RuleFor(request => request.Permissions!.OwnerId).NotEmpty()
            .When(request => request.Permissions is not null);
        RuleFor(request => request.Permissions!.ManagerIds).NotNull()
            .When(request => request.Permissions is not null);
        RuleFor(request => request.Permissions!.JudgeIds).NotNull()
            .When(request => request.Permissions is not null);
        RuleFor(request => request.Permissions!.ObserverIds).NotNull()
            .When(request => request.Permissions is not null);
        RuleFor(request => request.Permissions)
            .Must(permissions => permissions is null
                || permissions.ManagerIds is not null
                && permissions.JudgeIds is not null
                && permissions.ObserverIds is not null
                && permissions.ManagerIds.Concat(permissions.JudgeIds)
                    .Concat(permissions.ObserverIds).Distinct().Count()
                    == permissions.ManagerIds.Count + permissions.JudgeIds.Count
                    + permissions.ObserverIds.Count)
            .WithMessage("Competition permission assignments must be mutually exclusive.");
        RuleFor(request => request.LeaderboardVisibility!.Reason).MaximumLength(500)
            .When(request => request.LeaderboardVisibility is not null);
    }
}

[Mapper(AutoUserMappings = false, RequiredMappingStrategy = RequiredMappingStrategy.Both,
    UseDeepCloning = true)]
public static partial class CompetitionPatchMapper
{
    [MapProperty(nameof(CompetitionMetadataPatchRequest.StartTime), nameof(Competition.StartAt))]
    [MapProperty(nameof(CompetitionMetadataPatchRequest.EndTime), nameof(Competition.EndAt))]
    [MapperIgnoreSource(nameof(CompetitionMetadataPatchRequest.AccessMode))]
    [MapperIgnoreSource(nameof(CompetitionMetadataPatchRequest.RuntimeAccessMode))]
    [MapperIgnoreTarget(nameof(Competition.AccessMode))]
    [MapperIgnoreTarget(nameof(Competition.RuntimeAccessMode))]
    [MapperIgnoreTarget(nameof(Competition.OwnerId))]
    [MapperIgnoreTarget(nameof(Competition.ManagerIds))]
    [MapperIgnoreTarget(nameof(Competition.JudgeIds))]
    [MapperIgnoreTarget(nameof(Competition.ObserverIds))]
    [MapperIgnoreTarget(nameof(Competition.ConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.WebhookConfiguration))]
    [MapperIgnoreTarget(nameof(Competition.TracksEnabled))]
    [MapperIgnoreTarget(nameof(Competition.TrackConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.FrozenStartAt))]
    [MapperIgnoreTarget(nameof(Competition.HiddenStartAt))]
    [MapperIgnoreTarget(nameof(Competition.Id))]
    [MapperIgnoreTarget(nameof(Competition.Mode))]
    [MapperIgnoreTarget(nameof(Competition.Status))]
    [MapperIgnoreTarget(nameof(Competition.PosterFileId))]
    [MapperIgnoreTarget(nameof(Competition.PosterFile))]
    [MapperIgnoreTarget(nameof(Competition.FlagDerivationSecret))]
    [MapperIgnoreTarget(nameof(Competition.CreatedAt))]
    [MapperIgnoreTarget(nameof(Competition.UpdatedAt))]
    [MapperIgnoreTarget(nameof(Competition.DeletedAt))]
    public static partial void ApplyMetadataAsModerator(
        CompetitionMetadataPatchRequest request,
        [MappingTarget] Competition target);

    [MapProperty(nameof(CompetitionConfigurationPatchRequest.Json), nameof(Competition.ConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.WebhookConfiguration))]
    [MapperIgnoreTarget(nameof(Competition.Title))]
    [MapperIgnoreTarget(nameof(Competition.Description))]
    [MapperIgnoreTarget(nameof(Competition.AccessMode))]
    [MapperIgnoreTarget(nameof(Competition.RuntimeAccessMode))]
    [MapperIgnoreTarget(nameof(Competition.TrafficCaptureEnabled))]
    [MapperIgnoreTarget(nameof(Competition.TrafficCaptureLimitBytes))]
    [MapperIgnoreTarget(nameof(Competition.OwnerId))]
    [MapperIgnoreTarget(nameof(Competition.ManagerIds))]
    [MapperIgnoreTarget(nameof(Competition.JudgeIds))]
    [MapperIgnoreTarget(nameof(Competition.ObserverIds))]
    [MapperIgnoreTarget(nameof(Competition.TracksEnabled))]
    [MapperIgnoreTarget(nameof(Competition.TrackConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.FrozenStartAt))]
    [MapperIgnoreTarget(nameof(Competition.HiddenStartAt))]
    [MapperIgnoreTarget(nameof(Competition.StartAt))]
    [MapperIgnoreTarget(nameof(Competition.EndAt))]
    [MapperIgnoreTarget(nameof(Competition.TeamRegistrationAutoApprove))]
    [MapperIgnoreTarget(nameof(Competition.AllowTeamRegistrationWhileRunning))]
    [MapperIgnoreTarget(nameof(Competition.PracticeModeEnabled))]
    [MapperIgnoreTarget(nameof(Competition.WriteUpSubmissionRequired))]
    [MapperIgnoreTarget(nameof(Competition.WriteUpSubmissionDeadlineHours))]
    [MapperIgnoreTarget(nameof(Competition.MaxTeamMembers))]
    [MapperIgnoreTarget(nameof(Competition.MaxConcurrentRuntimeInstancesPerTeam))]
    [MapperIgnoreTarget(nameof(Competition.MaxActiveQuestionsPerTeam))]
    [MapperIgnoreTarget(nameof(Competition.MaxParticipantMessagesBeforeHandlerReply))]
    [MapperIgnoreTarget(nameof(Competition.AllowChallengeOwnersToHandleQuestions))]
    [MapperIgnoreTarget(nameof(Competition.Id))]
    [MapperIgnoreTarget(nameof(Competition.Mode))]
    [MapperIgnoreTarget(nameof(Competition.Status))]
    [MapperIgnoreTarget(nameof(Competition.PosterFileId))]
    [MapperIgnoreTarget(nameof(Competition.PosterFile))]
    [MapperIgnoreTarget(nameof(Competition.FlagDerivationSecret))]
    [MapperIgnoreTarget(nameof(Competition.CreatedAt))]
    [MapperIgnoreTarget(nameof(Competition.UpdatedAt))]
    [MapperIgnoreTarget(nameof(Competition.DeletedAt))]
    public static partial void ApplyConfigurationAsModerator(
        CompetitionConfigurationPatchRequest request,
        [MappingTarget] Competition target);

    [MapperIgnoreSource(nameof(CompetitionTracksPatchRequest.Tracks))]
    [MapperIgnoreSource(nameof(CompetitionTracksPatchRequest.RemovedTrackReassignments))]
    [MapProperty(nameof(CompetitionTracksPatchRequest.Enabled), nameof(Competition.TracksEnabled))]
    [MapProperty(nameof(CompetitionTracksPatchRequest.TrackConfigurationJson), nameof(Competition.TrackConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.Title))]
    [MapperIgnoreTarget(nameof(Competition.Description))]
    [MapperIgnoreTarget(nameof(Competition.AccessMode))]
    [MapperIgnoreTarget(nameof(Competition.RuntimeAccessMode))]
    [MapperIgnoreTarget(nameof(Competition.TrafficCaptureEnabled))]
    [MapperIgnoreTarget(nameof(Competition.TrafficCaptureLimitBytes))]
    [MapperIgnoreTarget(nameof(Competition.OwnerId))]
    [MapperIgnoreTarget(nameof(Competition.ManagerIds))]
    [MapperIgnoreTarget(nameof(Competition.JudgeIds))]
    [MapperIgnoreTarget(nameof(Competition.ObserverIds))]
    [MapperIgnoreTarget(nameof(Competition.ConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.WebhookConfiguration))]
    [MapperIgnoreTarget(nameof(Competition.FrozenStartAt))]
    [MapperIgnoreTarget(nameof(Competition.HiddenStartAt))]
    [MapperIgnoreTarget(nameof(Competition.StartAt))]
    [MapperIgnoreTarget(nameof(Competition.EndAt))]
    [MapperIgnoreTarget(nameof(Competition.TeamRegistrationAutoApprove))]
    [MapperIgnoreTarget(nameof(Competition.AllowTeamRegistrationWhileRunning))]
    [MapperIgnoreTarget(nameof(Competition.PracticeModeEnabled))]
    [MapperIgnoreTarget(nameof(Competition.WriteUpSubmissionRequired))]
    [MapperIgnoreTarget(nameof(Competition.WriteUpSubmissionDeadlineHours))]
    [MapperIgnoreTarget(nameof(Competition.MaxTeamMembers))]
    [MapperIgnoreTarget(nameof(Competition.MaxConcurrentRuntimeInstancesPerTeam))]
    [MapperIgnoreTarget(nameof(Competition.MaxActiveQuestionsPerTeam))]
    [MapperIgnoreTarget(nameof(Competition.MaxParticipantMessagesBeforeHandlerReply))]
    [MapperIgnoreTarget(nameof(Competition.AllowChallengeOwnersToHandleQuestions))]
    [MapperIgnoreTarget(nameof(Competition.Id))]
    [MapperIgnoreTarget(nameof(Competition.Mode))]
    [MapperIgnoreTarget(nameof(Competition.Status))]
    [MapperIgnoreTarget(nameof(Competition.PosterFileId))]
    [MapperIgnoreTarget(nameof(Competition.PosterFile))]
    [MapperIgnoreTarget(nameof(Competition.FlagDerivationSecret))]
    [MapperIgnoreTarget(nameof(Competition.CreatedAt))]
    [MapperIgnoreTarget(nameof(Competition.UpdatedAt))]
    [MapperIgnoreTarget(nameof(Competition.DeletedAt))]
    public static partial void ApplyTracksAsModerator(
        CompetitionTracksPatchRequest request,
        [MappingTarget] Competition target);

    [MapperIgnoreTarget(nameof(Competition.Title))]
    [MapperIgnoreTarget(nameof(Competition.Description))]
    [MapperIgnoreTarget(nameof(Competition.AccessMode))]
    [MapperIgnoreTarget(nameof(Competition.RuntimeAccessMode))]
    [MapperIgnoreTarget(nameof(Competition.TrafficCaptureEnabled))]
    [MapperIgnoreTarget(nameof(Competition.TrafficCaptureLimitBytes))]
    [MapperIgnoreTarget(nameof(Competition.ConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.WebhookConfiguration))]
    [MapperIgnoreTarget(nameof(Competition.TracksEnabled))]
    [MapperIgnoreTarget(nameof(Competition.TrackConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.FrozenStartAt))]
    [MapperIgnoreTarget(nameof(Competition.HiddenStartAt))]
    [MapperIgnoreTarget(nameof(Competition.StartAt))]
    [MapperIgnoreTarget(nameof(Competition.EndAt))]
    [MapperIgnoreTarget(nameof(Competition.TeamRegistrationAutoApprove))]
    [MapperIgnoreTarget(nameof(Competition.AllowTeamRegistrationWhileRunning))]
    [MapperIgnoreTarget(nameof(Competition.PracticeModeEnabled))]
    [MapperIgnoreTarget(nameof(Competition.WriteUpSubmissionRequired))]
    [MapperIgnoreTarget(nameof(Competition.WriteUpSubmissionDeadlineHours))]
    [MapperIgnoreTarget(nameof(Competition.MaxTeamMembers))]
    [MapperIgnoreTarget(nameof(Competition.MaxConcurrentRuntimeInstancesPerTeam))]
    [MapperIgnoreTarget(nameof(Competition.MaxActiveQuestionsPerTeam))]
    [MapperIgnoreTarget(nameof(Competition.MaxParticipantMessagesBeforeHandlerReply))]
    [MapperIgnoreTarget(nameof(Competition.AllowChallengeOwnersToHandleQuestions))]
    [MapperIgnoreTarget(nameof(Competition.Id))]
    [MapperIgnoreTarget(nameof(Competition.Mode))]
    [MapperIgnoreTarget(nameof(Competition.Status))]
    [MapperIgnoreTarget(nameof(Competition.PosterFileId))]
    [MapperIgnoreTarget(nameof(Competition.PosterFile))]
    [MapperIgnoreTarget(nameof(Competition.FlagDerivationSecret))]
    [MapperIgnoreTarget(nameof(Competition.CreatedAt))]
    [MapperIgnoreTarget(nameof(Competition.UpdatedAt))]
    [MapperIgnoreTarget(nameof(Competition.DeletedAt))]
    public static partial void ApplyPermissionsAsOwner(
        CompetitionPermissionsPatchRequest request,
        [MappingTarget] Competition target);

    [MapperIgnoreSource(nameof(CompetitionLeaderboardPatchRequest.Reason))]
    [MapperIgnoreTarget(nameof(Competition.Title))]
    [MapperIgnoreTarget(nameof(Competition.Description))]
    [MapperIgnoreTarget(nameof(Competition.AccessMode))]
    [MapperIgnoreTarget(nameof(Competition.RuntimeAccessMode))]
    [MapperIgnoreTarget(nameof(Competition.TrafficCaptureEnabled))]
    [MapperIgnoreTarget(nameof(Competition.TrafficCaptureLimitBytes))]
    [MapperIgnoreTarget(nameof(Competition.OwnerId))]
    [MapperIgnoreTarget(nameof(Competition.ManagerIds))]
    [MapperIgnoreTarget(nameof(Competition.JudgeIds))]
    [MapperIgnoreTarget(nameof(Competition.ObserverIds))]
    [MapperIgnoreTarget(nameof(Competition.ConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.WebhookConfiguration))]
    [MapperIgnoreTarget(nameof(Competition.TracksEnabled))]
    [MapperIgnoreTarget(nameof(Competition.TrackConfigurationJson))]
    [MapperIgnoreTarget(nameof(Competition.StartAt))]
    [MapperIgnoreTarget(nameof(Competition.EndAt))]
    [MapperIgnoreTarget(nameof(Competition.TeamRegistrationAutoApprove))]
    [MapperIgnoreTarget(nameof(Competition.AllowTeamRegistrationWhileRunning))]
    [MapperIgnoreTarget(nameof(Competition.PracticeModeEnabled))]
    [MapperIgnoreTarget(nameof(Competition.WriteUpSubmissionRequired))]
    [MapperIgnoreTarget(nameof(Competition.WriteUpSubmissionDeadlineHours))]
    [MapperIgnoreTarget(nameof(Competition.MaxTeamMembers))]
    [MapperIgnoreTarget(nameof(Competition.MaxConcurrentRuntimeInstancesPerTeam))]
    [MapperIgnoreTarget(nameof(Competition.MaxActiveQuestionsPerTeam))]
    [MapperIgnoreTarget(nameof(Competition.MaxParticipantMessagesBeforeHandlerReply))]
    [MapperIgnoreTarget(nameof(Competition.AllowChallengeOwnersToHandleQuestions))]
    [MapperIgnoreTarget(nameof(Competition.Id))]
    [MapperIgnoreTarget(nameof(Competition.Mode))]
    [MapperIgnoreTarget(nameof(Competition.Status))]
    [MapperIgnoreTarget(nameof(Competition.PosterFileId))]
    [MapperIgnoreTarget(nameof(Competition.PosterFile))]
    [MapperIgnoreTarget(nameof(Competition.FlagDerivationSecret))]
    [MapperIgnoreTarget(nameof(Competition.CreatedAt))]
    [MapperIgnoreTarget(nameof(Competition.UpdatedAt))]
    [MapperIgnoreTarget(nameof(Competition.DeletedAt))]
    public static partial void ApplyLeaderboardAsModerator(
        CompetitionLeaderboardPatchRequest request,
        [MappingTarget] Competition target);
}

public sealed class PatchCompetitionEndpoint(
    GetAdminCompetition get,
    GetCompetitionConfiguration getConfiguration,
    GetCompetitionTracks getTracks,
    GetCompetitionPermissions getPermissions,
    GetCompetitionVisibility getVisibility,
    UpdateCompetition update,
    UpdateCompetitionConfiguration updateConfiguration,
    UpdateCompetitionTracks updateTracks,
    UpdateCompetitionPermissions updatePermissions,
    TransferCompetitionOwner transferOwner,
    UpdateCompetitionVisibility updateVisibility,
    IAtomicAggregatePatch atomicPatch,
    ICompetitionModerationAuthorizer authorizer,
    ManageSsoProviders ssoProviders,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<PatchCompetitionRequest,
        Results<Ok<AdminCompetitionResponse>, NotFound, ForbidHttpResult,
            Conflict<CompetitionTrackFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/competitions/{competitionId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminPatchCompetition"));
        Summary(summary => summary.Summary = "Updates selected competition aggregate sections.");
    }

    public override async Task<Results<Ok<AdminCompetitionResponse>, NotFound,
        ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        PatchCompetitionRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var sections = ResolveSections(request);
        var nonPermissionPatch = (sections & ~CompetitionPatchSection.Permissions) != 0;
        if (nonPermissionPatch
            && !await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var current = await get.ExecuteAsync(
            competitionId, user.UserId, user.IsAdministrator,
            includeDeleted: false, ct);
        var configuration = await getConfiguration.ExecuteAsync(competitionId, ct);
        var permissions = await getPermissions.ExecuteAsync(
            competitionId, user.UserId, user.IsAdministrator, ct);
        if (current is null || configuration is null)
            return TypedResults.NotFound();
        if ((sections & CompetitionPatchSection.Permissions) != 0
            && permissions.State != CompetitionPermissionSnapshotState.Found)
            return TypedResults.Forbid();

        if ((sections & CompetitionPatchSection.Tracks) != 0)
        {
            request.Tracks!.TrackConfigurationJson = CompetitionTrackConfiguration.Serialize(new(
                CompetitionTrackConfiguration.CurrentSchemaVersion,
                request.Tracks.Tracks.Select(ToDefinition).ToArray()));
        }
        var target = new Competition
        {
            Id = current.Id,
            Title = current.Title,
            Description = current.Description,
            OwnerId = current.OwnerId,
            AccessMode = current.AccessMode,
            ManagerIds = permissions.Snapshot?.ManagerIds.ToArray() ?? [],
            JudgeIds = permissions.Snapshot?.JudgeIds.ToArray() ?? [],
            ObserverIds = permissions.Snapshot?.ObserverIds.ToArray() ?? [],
            Mode = current.Mode,
            ConfigurationJson = configuration.Json,
            TracksEnabled = current.TracksEnabled,
            FrozenStartAt = current.FrozenStartAt,
            HiddenStartAt = current.HiddenStartAt,
            StartAt = current.StartTime,
            EndAt = current.EndTime,
            Status = current.Status,
            TeamRegistrationAutoApprove = current.TeamRegistrationAutoApprove,
            AllowTeamRegistrationWhileRunning = current.AllowTeamRegistrationWhileRunning,
            PracticeModeEnabled = current.PracticeModeEnabled,
            WriteUpSubmissionRequired = current.WriteUpSubmissionRequired,
            WriteUpSubmissionDeadlineHours = current.WriteUpSubmissionDeadlineHours,
            MaxTeamMembers = current.MaxTeamMembers,
            MaxConcurrentRuntimeInstancesPerTeam = current.MaxConcurrentRuntimeInstancesPerTeam,
            MaxActiveQuestionsPerTeam = current.MaxActiveQuestionsPerTeam,
            MaxParticipantMessagesBeforeHandlerReply = current.MaxParticipantMessagesBeforeHandlerReply,
            AllowChallengeOwnersToHandleQuestions = current.AllowChallengeOwnersToHandleQuestions,
            RuntimeAccessMode = current.RuntimeAccessMode,
            TrafficCaptureEnabled = current.TrafficCaptureEnabled,
            TrafficCaptureLimitBytes = current.TrafficCaptureLimitBytes,
            DeletedAt = current.DeletedAt
        };
        if ((sections & CompetitionPatchSection.Metadata) != 0)
        {
            CompetitionPatchMapper.ApplyMetadataAsModerator(request.Metadata!, target);
            target.AccessMode = CompetitionProtocolMapper.ToDomain(request.Metadata!.AccessMode);
            target.RuntimeAccessMode = CompetitionProtocolMapper.ToDomain(
                request.Metadata.RuntimeAccessMode);
        }
        if ((sections & CompetitionPatchSection.ModeConfiguration) != 0)
            CompetitionPatchMapper.ApplyConfigurationAsModerator(request.ModeConfiguration!, target);
        if ((sections & CompetitionPatchSection.Tracks) != 0)
            CompetitionPatchMapper.ApplyTracksAsModerator(request.Tracks!, target);
        if ((sections & CompetitionPatchSection.Permissions) != 0)
            CompetitionPatchMapper.ApplyPermissionsAsOwner(request.Permissions!, target);
        if ((sections & CompetitionPatchSection.LeaderboardVisibility) != 0)
            CompetitionPatchMapper.ApplyLeaderboardAsModerator(
                request.LeaderboardVisibility!, target);

        if ((sections & CompetitionPatchSection.Permissions) != 0
            && target.OwnerId != current.OwnerId
            && !target.ManagerIds.Contains(current.OwnerId))
            return Failure("The previous owner must remain in ManagerIds when ownership changes.");

        return await atomicPatch.ExecuteAsync(ApplyAsync, ct);

        async Task<AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionResponse>,
            NotFound, ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>,
            ProblemHttpResult>>> ApplyAsync(
            CancellationToken transactionCt)
        {
            if ((sections & CompetitionPatchSection.Metadata) != 0)
            {
                var result = await update.ExecuteAsync(new UpdateCompetitionCommand(
                    competitionId,
                    target.Title,
                    target.Description,
                    target.StartAt,
                    target.EndAt,
                    target.TeamRegistrationAutoApprove,
                    target.MaxTeamMembers,
                    target.MaxConcurrentRuntimeInstancesPerTeam,
                    user.UserId,
                    timeProvider.GetUtcNow(),
                    target.AllowTeamRegistrationWhileRunning,
                    target.MaxActiveQuestionsPerTeam,
                    target.MaxParticipantMessagesBeforeHandlerReply,
                    target.AllowChallengeOwnersToHandleQuestions,
                    target.PracticeModeEnabled,
                    target.AccessMode,
                    target.WriteUpSubmissionRequired,
                    target.WriteUpSubmissionDeadlineHours,
                    target.RuntimeAccessMode,
                    target.TrafficCaptureEnabled,
                    target.TrafficCaptureLimitBytes), transactionCt);
                if (!result.Succeeded)
                    return Reject(Failure(
                        result.ErrorMessage ?? "Competition metadata is invalid."));
            }
            if ((sections & CompetitionPatchSection.ModeConfiguration) != 0)
            {
                var result = await updateConfiguration.ExecuteAsync(
                    competitionId,
                    target.ConfigurationJson,
                    timeProvider.GetUtcNow(),
                    transactionCt);
                if (!result.Succeeded)
                    return Reject(Failure(
                        result.ErrorMessage ?? "Competition configuration is invalid."));
            }
            if ((sections & CompetitionPatchSection.Tracks) != 0)
            {
                var tracks = await getTracks.ExecuteAsync(
                    competitionId,
                    user.UserId,
                    true,
                    true,
                    transactionCt);
                if (tracks is null)
                    return Missing();
                var result = await updateTracks.ExecuteAsync(new UpdateCompetitionTracksCommand(
                    competitionId,
                    request.Tracks!.Enabled,
                    request.Tracks!.Tracks.Select(ToDefinition).ToArray(),
                    request.Tracks.RemovedTrackReassignments.Select(reassignment =>
                        new RemovedTrackReassignment(
                            reassignment.FromTrackKey,
                            reassignment.ToTrackKey)).ToArray(),
                    user.UserId,
                    timeProvider.GetUtcNow(),
                    request.Tracks.Tracks.Select(track =>
                        new CompetitionTrackInvitationCodeUpdate(
                            track.Key,
                            track.InvitationCode,
                            track.ClearInvitationCode)).ToArray()),
                    tracks.Mode,
                    transactionCt);
                if (!result.Succeeded)
                    return RejectTrack(TrackFailure(result));
            }
            if ((sections & CompetitionPatchSection.Permissions) != 0)
            {
                if (target.OwnerId != current.OwnerId)
                {
                    var transfer = await transferOwner.ExecuteAsync(
                        competitionId,
                        user.UserId,
                        user.IsAdministrator,
                        target.OwnerId,
                        timeProvider.GetUtcNow(),
                        transactionCt);
                    if (transfer.State != CompetitionOwnerTransferState.Transferred)
                        return Reject(Failure(transfer.State.ToString()));
                }
                var result = await updatePermissions.ExecuteAsync(
                    new UpdateCompetitionPermissionsCommand(
                        competitionId,
                        user.UserId,
                        target.ManagerIds,
                        target.JudgeIds,
                        target.ObserverIds),
                    transactionCt);
                if (result.State != CompetitionPermissionUpdateState.Updated)
                    return Reject(Failure(result.State.ToString()));
            }
            if ((sections & CompetitionPatchSection.LeaderboardVisibility) != 0)
            {
                var result = await updateVisibility.ExecuteAsync(
                    new UpdateCompetitionVisibilityCommand(
                        competitionId,
                        target.FrozenStartAt,
                        target.HiddenStartAt,
                        user.UserId,
                        request.LeaderboardVisibility!.Reason,
                        timeProvider.GetUtcNow()),
                    transactionCt);
                if (!result.Succeeded)
                    return Reject(Failure(result.State.ToString()));
            }

            var response = await LoadResponseAsync(competitionId, transactionCt);
            Results<Ok<AdminCompetitionResponse>, NotFound, ForbidHttpResult,
                Conflict<CompetitionTrackFailureResponse>, ProblemHttpResult> outcome = response is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(response);
            return AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionResponse>,
                NotFound, ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>,
                ProblemHttpResult>>.Commit(outcome);
        }

        static AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionResponse>,
            NotFound, ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>,
            ProblemHttpResult>> Reject(
            ProblemHttpResult failure) =>
            AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionResponse>,
                NotFound, ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>,
                ProblemHttpResult>>.Rollback(failure);

        static AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionResponse>,
            NotFound, ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>,
            ProblemHttpResult>> RejectTrack(
            Conflict<CompetitionTrackFailureResponse> failure) =>
            AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionResponse>,
                NotFound, ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>,
                ProblemHttpResult>>.Rollback(failure);

        static AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionResponse>,
            NotFound, ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>,
            ProblemHttpResult>> Missing() =>
            AtomicAggregatePatchDecision<Results<Ok<AdminCompetitionResponse>,
                NotFound, ForbidHttpResult, Conflict<CompetitionTrackFailureResponse>,
                ProblemHttpResult>>.Rollback(
                    TypedResults.NotFound());
    }

    private async Task<AdminCompetitionResponse?> LoadResponseAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var view = await get.ExecuteAsync(
            competitionId, user.UserId, user.IsAdministrator, false, ct);
        if (view is null)
            return null;
        var canModerate = await authorizer.CanModerateAsync(user.UserId, competitionId, ct);
        var configuration = await getConfiguration.ExecuteAsync(competitionId, ct);
        var tracks = await getTracks.ExecuteAsync(
            competitionId, user.UserId, true, canModerate, ct);
        var permissions = await getPermissions.ExecuteAsync(
            competitionId, user.UserId, user.IsAdministrator, ct);
        var visibility = await getVisibility.ExecuteAsync(
            competitionId, timeProvider.GetUtcNow(), ct);
        var sso = await ssoProviders.GetAsync(ct);
        if (configuration is null || tracks is null || visibility is null)
            return null;
        var role = await CompetitionAdministrationRoleResolver.ResolveAsync(
            view, user, authorizer, ct, accessAlreadyEstablished: true);
        return new(
            CompetitionMapper.ToResponse(view, timeProvider.GetUtcNow()) with
            {
                AdministrationRole = role
            },
            CompetitionConfigurationMapping.ToResponse(configuration),
            CompetitionTrackProtocolMapping.ToResponse(tracks),
            permissions.State == CompetitionPermissionSnapshotState.Found
                ? CompetitionPermissionsMapper.ToResponse(permissions.Snapshot!)
                : null,
            CompetitionLeaderboardVisibilityMapper.ToResponse(visibility),
            sso.Providers.Select(provider => new CompetitionSsoProviderResponse(
                provider.Id,
                provider.Name,
                provider.IconUrl,
                provider.Protocol == NoCTF.Domain.Identity.SsoProtocol.Oidc
                    ? PublicSsoProtocol.Oidc
                    : PublicSsoProtocol.Cas,
                provider.Enabled,
                provider.AllowBinding)).ToArray(),
            new(true, canModerate,
                permissions.State == CompetitionPermissionSnapshotState.Found));
    }

    private static CompetitionTrackDefinition ToDefinition(UpdateCompetitionTrackRequest track) =>
        new(
            track.Key,
            track.Name,
            track.IsDefault,
            track.IsPublicSelectable,
            track.IsInternal,
            track.EarnsScore,
            track.EarnsBlood,
            track.AffectsDynamicChallengeScore,
            track.VisibleOnLeaderboard,
            track.AffectsCompetitiveResults,
            track.InvitationCode,
            track.RequiredSsoProviderId);

    private static CompetitionPatchSection ResolveSections(PatchCompetitionRequest request) =>
        (request.Metadata is null ? CompetitionPatchSection.None
            : CompetitionPatchSection.Metadata)
        | (request.ModeConfiguration is null ? CompetitionPatchSection.None
            : CompetitionPatchSection.ModeConfiguration)
        | (request.Tracks is null ? CompetitionPatchSection.None
            : CompetitionPatchSection.Tracks)
        | (request.Permissions is null ? CompetitionPatchSection.None
            : CompetitionPatchSection.Permissions)
        | (request.LeaderboardVisibility is null ? CompetitionPatchSection.None
            : CompetitionPatchSection.LeaderboardVisibility);

    private static ProblemHttpResult Failure(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Competition was not updated.",
        detail: detail);

    private static Conflict<CompetitionTrackFailureResponse> TrackFailure(
        UpdateCompetitionTracksResult result) =>
        TypedResults.Conflict(new CompetitionTrackFailureResponse(
            CompetitionTrackFailureProtocolMapper.ToProtocol(result.FailureCode!.Value),
            result.ErrorMessage ?? "Competition tracks were not updated.",
            result.AffectedTeamCount));
}
