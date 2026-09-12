using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Common;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Teams;

public sealed class TeamProfilePatchRequest
{
    public required string Name { get; set; }
}

public sealed class TeamMembershipPatchRequest
{
    public required Guid CaptainId { get; set; }
    public required IReadOnlyList<Guid> MemberIds { get; set; }
}

public sealed class TeamRegistrationPatchRequest
{
    public required TeamRegistrationStatusProtocol Status { get; set; }
}

public sealed class TeamAdministrationPatchRequest
{
    public required string TrackKey { get; set; }
    public required TeamRegistrationStatusProtocol RegistrationStatus { get; set; }
}

public sealed class TeamBanPatchRequest
{
    public required bool IsBanned { get; set; }
    public required string? Reason { get; set; }
    public bool AnnouncePublicly { get; set; }
}

public sealed class PatchTeamRequest
{
    public TeamProfilePatchRequest? Profile { get; set; }
    public TeamMembershipPatchRequest? Membership { get; set; }
    public TeamRegistrationPatchRequest? Registration { get; set; }
    public TeamAdministrationPatchRequest? Administration { get; set; }
    public TeamBanPatchRequest? Ban { get; set; }
}

[Flags]
internal enum TeamPatchSection
{
    None = 0,
    Profile = 1 << 0,
    Membership = 1 << 1,
    Registration = 1 << 2,
    Administration = 1 << 3,
    Ban = 1 << 4
}

public sealed class PatchTeamValidator : Validator<PatchTeamRequest>
{
    public PatchTeamValidator()
    {
        RuleFor(request => request).Must(request => request.Profile is not null
            || request.Membership is not null
            || request.Registration is not null
            || request.Administration is not null
            || request.Ban is not null)
            .WithMessage("At least one team section is required.");
        RuleFor(request => request.Profile!.Name).NotEmpty().MaximumLength(128)
            .When(request => request.Profile is not null);
        RuleFor(request => request.Membership!.CaptainId).NotEmpty()
            .When(request => request.Membership is not null);
        RuleFor(request => request.Membership!.MemberIds).NotEmpty()
            .Must(ids => ids.Count == ids.Distinct().Count())
            .When(request => request.Membership is not null);
        RuleForEach(request => request.Membership!.MemberIds).NotEmpty()
            .When(request => request.Membership is not null);
        RuleFor(request => request.Registration!.Status).IsInEnum()
            .When(request => request.Registration is not null);
        RuleFor(request => request.Administration!.TrackKey).NotEmpty().MaximumLength(64)
            .When(request => request.Administration is not null);
        RuleFor(request => request.Administration!.RegistrationStatus).IsInEnum()
            .When(request => request.Administration is not null);
        RuleFor(request => request.Registration!.Status)
            .Equal(TeamRegistrationStatusProtocol.Pending)
            .When(request => request.Registration is not null)
            .WithMessage("Captain registration updates can only resubmit a rejected team as Pending.");
        RuleFor(request => request.Ban!.Reason).NotEmpty().MaximumLength(512)
            .When(request => request.Ban?.IsBanned == true);
    }
}

[Mapper(
    AutoUserMappings = false,
    RequiredMappingStrategy = RequiredMappingStrategy.Both,
    UseDeepCloning = true)]
public static partial class TeamPatchMapper
{
    [MapperIgnoreTarget(nameof(Team.Id))]
    [MapperIgnoreTarget(nameof(Team.CompetitionId))]
    [MapperIgnoreTarget(nameof(Team.TrackKey))]
    [MapperIgnoreTarget(nameof(Team.AvatarFileId))]
    [MapperIgnoreTarget(nameof(Team.AvatarFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFileId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedByUserId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedAt))]
    [MapperIgnoreTarget(nameof(Team.CaptainId))]
    [MapperIgnoreTarget(nameof(Team.MemberIds))]
    [MapperIgnoreTarget(nameof(Team.InvitationToken))]
    [MapperIgnoreTarget(nameof(Team.IsLocked))]
    [MapperIgnoreTarget(nameof(Team.RegistrationStatus))]
    [MapperIgnoreTarget(nameof(Team.RegisteredAt))]
    [MapperIgnoreTarget(nameof(Team.IsBanned))]
    [MapperIgnoreTarget(nameof(Team.BannedAt))]
    [MapperIgnoreTarget(nameof(Team.BannedById))]
    [MapperIgnoreTarget(nameof(Team.BanReason))]
    [MapperIgnoreTarget(nameof(Team.DeletedAt))]
    public static partial void ApplyProfileAsCaptain(
        TeamProfilePatchRequest request,
        [MappingTarget] Team target);

    [MapperIgnoreTarget(nameof(Team.Id))]
    [MapperIgnoreTarget(nameof(Team.CompetitionId))]
    [MapperIgnoreTarget(nameof(Team.TrackKey))]
    [MapperIgnoreTarget(nameof(Team.Name))]
    [MapperIgnoreTarget(nameof(Team.AvatarFileId))]
    [MapperIgnoreTarget(nameof(Team.AvatarFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFileId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedByUserId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedAt))]
    [MapperIgnoreTarget(nameof(Team.InvitationToken))]
    [MapperIgnoreTarget(nameof(Team.IsLocked))]
    [MapperIgnoreTarget(nameof(Team.RegistrationStatus))]
    [MapperIgnoreTarget(nameof(Team.RegisteredAt))]
    [MapperIgnoreTarget(nameof(Team.IsBanned))]
    [MapperIgnoreTarget(nameof(Team.BannedAt))]
    [MapperIgnoreTarget(nameof(Team.BannedById))]
    [MapperIgnoreTarget(nameof(Team.BanReason))]
    [MapperIgnoreTarget(nameof(Team.DeletedAt))]
    public static partial void ApplyMembershipAsCaptain(
        TeamMembershipPatchRequest request,
        [MappingTarget] Team target);

    [MapProperty(nameof(TeamRegistrationPatchRequest.Status), nameof(Team.RegistrationStatus))]
    [MapperIgnoreTarget(nameof(Team.Id))]
    [MapperIgnoreTarget(nameof(Team.CompetitionId))]
    [MapperIgnoreTarget(nameof(Team.TrackKey))]
    [MapperIgnoreTarget(nameof(Team.Name))]
    [MapperIgnoreTarget(nameof(Team.AvatarFileId))]
    [MapperIgnoreTarget(nameof(Team.AvatarFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFileId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedByUserId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedAt))]
    [MapperIgnoreTarget(nameof(Team.CaptainId))]
    [MapperIgnoreTarget(nameof(Team.MemberIds))]
    [MapperIgnoreTarget(nameof(Team.InvitationToken))]
    [MapperIgnoreTarget(nameof(Team.IsLocked))]
    [MapperIgnoreTarget(nameof(Team.RegisteredAt))]
    [MapperIgnoreTarget(nameof(Team.IsBanned))]
    [MapperIgnoreTarget(nameof(Team.BannedAt))]
    [MapperIgnoreTarget(nameof(Team.BannedById))]
    [MapperIgnoreTarget(nameof(Team.BanReason))]
    [MapperIgnoreTarget(nameof(Team.DeletedAt))]
    public static partial void ApplyRegistrationAsCaptain(
        TeamRegistrationPatchRequest request,
        [MappingTarget] Team target);

    [MapProperty(nameof(TeamAdministrationPatchRequest.RegistrationStatus),
        nameof(Team.RegistrationStatus))]
    [MapperIgnoreTarget(nameof(Team.Id))]
    [MapperIgnoreTarget(nameof(Team.CompetitionId))]
    [MapperIgnoreTarget(nameof(Team.Name))]
    [MapperIgnoreTarget(nameof(Team.AvatarFileId))]
    [MapperIgnoreTarget(nameof(Team.AvatarFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFileId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedByUserId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedAt))]
    [MapperIgnoreTarget(nameof(Team.CaptainId))]
    [MapperIgnoreTarget(nameof(Team.MemberIds))]
    [MapperIgnoreTarget(nameof(Team.InvitationToken))]
    [MapperIgnoreTarget(nameof(Team.IsLocked))]
    [MapperIgnoreTarget(nameof(Team.RegisteredAt))]
    [MapperIgnoreTarget(nameof(Team.IsBanned))]
    [MapperIgnoreTarget(nameof(Team.BannedAt))]
    [MapperIgnoreTarget(nameof(Team.BannedById))]
    [MapperIgnoreTarget(nameof(Team.BanReason))]
    [MapperIgnoreTarget(nameof(Team.DeletedAt))]
    public static partial void ApplyAdministrationAsModerator(
        TeamAdministrationPatchRequest request,
        [MappingTarget] Team target);

    [MapProperty(nameof(TeamBanPatchRequest.Reason), nameof(Team.BanReason))]
    [MapperIgnoreSource(nameof(TeamBanPatchRequest.AnnouncePublicly))]
    [MapperIgnoreTarget(nameof(Team.Id))]
    [MapperIgnoreTarget(nameof(Team.CompetitionId))]
    [MapperIgnoreTarget(nameof(Team.TrackKey))]
    [MapperIgnoreTarget(nameof(Team.Name))]
    [MapperIgnoreTarget(nameof(Team.AvatarFileId))]
    [MapperIgnoreTarget(nameof(Team.AvatarFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFileId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedByUserId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedAt))]
    [MapperIgnoreTarget(nameof(Team.CaptainId))]
    [MapperIgnoreTarget(nameof(Team.MemberIds))]
    [MapperIgnoreTarget(nameof(Team.InvitationToken))]
    [MapperIgnoreTarget(nameof(Team.IsLocked))]
    [MapperIgnoreTarget(nameof(Team.RegistrationStatus))]
    [MapperIgnoreTarget(nameof(Team.RegisteredAt))]
    [MapperIgnoreTarget(nameof(Team.BannedAt))]
    [MapperIgnoreTarget(nameof(Team.BannedById))]
    [MapperIgnoreTarget(nameof(Team.DeletedAt))]
    public static partial void ApplyBanAsJudge(
        TeamBanPatchRequest request,
        [MappingTarget] Team target);

    [MapProperty(nameof(TeamBanPatchRequest.Reason), nameof(Team.BanReason))]
    [MapperIgnoreSource(nameof(TeamBanPatchRequest.AnnouncePublicly))]
    [MapperIgnoreTarget(nameof(Team.Id))]
    [MapperIgnoreTarget(nameof(Team.CompetitionId))]
    [MapperIgnoreTarget(nameof(Team.TrackKey))]
    [MapperIgnoreTarget(nameof(Team.Name))]
    [MapperIgnoreTarget(nameof(Team.AvatarFileId))]
    [MapperIgnoreTarget(nameof(Team.AvatarFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFileId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpFile))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedByUserId))]
    [MapperIgnoreTarget(nameof(Team.WriteUpSubmittedAt))]
    [MapperIgnoreTarget(nameof(Team.CaptainId))]
    [MapperIgnoreTarget(nameof(Team.MemberIds))]
    [MapperIgnoreTarget(nameof(Team.InvitationToken))]
    [MapperIgnoreTarget(nameof(Team.IsLocked))]
    [MapperIgnoreTarget(nameof(Team.RegistrationStatus))]
    [MapperIgnoreTarget(nameof(Team.RegisteredAt))]
    [MapperIgnoreTarget(nameof(Team.BannedAt))]
    [MapperIgnoreTarget(nameof(Team.BannedById))]
    [MapperIgnoreTarget(nameof(Team.DeletedAt))]
    public static partial void ApplyUnbanAsModerator(
        TeamBanPatchRequest request,
        [MappingTarget] Team target);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial TeamRegistrationStatus ToDomain(TeamRegistrationStatusProtocol value);
}

public sealed class PatchTeamEndpoint(
    GetTeam get,
    UpdateTeam update,
    TransferTeamCaptain transferCaptain,
    RemoveTeamMember removeMember,
    ReviewTeamRegistration reviewRegistration,
    ResubmitTeamRegistration resubmitRegistration,
    AssignTeamTrack assignTrack,
    ModerateTeam moderate,
    IAtomicAggregatePatch atomicPatch,
    ITeamRegistrationStore teams,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    LinkGenerator links,
    TimeProvider timeProvider)
    : Endpoint<PatchTeamRequest,
        Results<Ok<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/competitions/{competitionId}/teams/{teamId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("PatchCompetitionTeam"));
        Summary(summary => summary.Summary = "Updates authorized sections of a competition team.");
    }

    public override async Task<Results<Ok<TeamResponse>, NotFound, ForbidHttpResult,
        ProblemHttpResult>> ExecuteAsync(PatchTeamRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        var sections = ResolveSections(request);
        var requiresCaptain = (sections & (TeamPatchSection.Profile
            | TeamPatchSection.Membership | TeamPatchSection.Registration)) != 0;
        if (requiresCaptain
            && !await teams.CanManageAsync(user.UserId, competitionId, teamId, ct))
            return TypedResults.Forbid();
        var requiresModerator = (sections & TeamPatchSection.Administration) != 0
            || request.Ban?.IsBanned == false;
        if (requiresModerator
            && !await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        if (request.Ban?.IsBanned == true
            && !await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var current = await get.ExecuteAsync(
            competitionId,
            teamId,
            includePending: true,
            includeInternal: true,
            ct);
        if (current is null)
            return TypedResults.NotFound();
        var target = new Team
        {
            Id = current.Id,
            CompetitionId = current.CompetitionId,
            TrackKey = current.TrackKey,
            Name = current.Name,
            AvatarFileId = current.AvatarFileId,
            CaptainId = current.CaptainId,
            MemberIds = current.MemberIds.ToArray(),
            RegistrationStatus = current.RegistrationStatus,
            IsLocked = current.IsLocked,
            RegisteredAt = current.RegisteredAt,
            IsBanned = current.IsBanned
        };
        if ((sections & TeamPatchSection.Profile) != 0)
            TeamPatchMapper.ApplyProfileAsCaptain(request.Profile!, target);
        if ((sections & TeamPatchSection.Membership) != 0)
            TeamPatchMapper.ApplyMembershipAsCaptain(request.Membership!, target);
        if ((sections & TeamPatchSection.Registration) != 0)
            TeamPatchMapper.ApplyRegistrationAsCaptain(request.Registration!, target);
        if ((sections & TeamPatchSection.Administration) != 0)
            TeamPatchMapper.ApplyAdministrationAsModerator(request.Administration!, target);
        if ((sections & TeamPatchSection.Ban) != 0)
        {
            if (request.Ban!.IsBanned)
                TeamPatchMapper.ApplyBanAsJudge(request.Ban, target);
            else
                TeamPatchMapper.ApplyUnbanAsModerator(request.Ban, target);
        }

        if ((sections & TeamPatchSection.Membership) != 0
            && (!target.MemberIds.Contains(target.CaptainId)
                || target.MemberIds.Except(current.MemberIds).Any()))
        {
            return Conflict("Team membership update is invalid.");
        }
        return await atomicPatch.ExecuteAsync(ApplyAsync, ct);

        async Task<AtomicAggregatePatchDecision<Results<Ok<TeamResponse>, NotFound,
            ForbidHttpResult, ProblemHttpResult>>> ApplyAsync(CancellationToken transactionCt)
        {
            if ((sections & TeamPatchSection.Profile) != 0)
            {
                var result = await update.ExecuteAsync(
                    new UpdateTeamCommand(competitionId, teamId, target.Name),
                    transactionCt);
                if (!result.Succeeded)
                    return Reject(MapFailure(result.FailureCode, result.ErrorMessage));
            }
            if ((sections & TeamPatchSection.Membership) != 0)
            {
                if (target.CaptainId != current.CaptainId)
                {
                    var result = await transferCaptain.ExecuteAsync(
                        competitionId,
                        teamId,
                        user.UserId,
                        target.CaptainId,
                        transactionCt);
                    if (!result.Succeeded)
                    {
                        return Reject(MapMembershipFailure(
                            result.FailureCode,
                            result.ErrorMessage));
                    }
                }
                foreach (var removedId in current.MemberIds.Except(target.MemberIds))
                {
                    var result = await removeMember.ExecuteAsync(
                        competitionId,
                        teamId,
                        removedId,
                        user.UserId,
                        transactionCt);
                    if (!result.Succeeded)
                    {
                        return Reject(MapMembershipFailure(
                            result.FailureCode,
                            result.ErrorMessage));
                    }
                }
            }
            if ((sections & TeamPatchSection.Registration) != 0)
            {
                var result = await resubmitRegistration.ExecuteAsync(
                    competitionId,
                    teamId,
                    user.UserId,
                    transactionCt);
                if (!result.Succeeded)
                    return Reject(MapFailure(result.FailureCode, result.ErrorMessage));
            }
            if ((sections & TeamPatchSection.Administration) != 0)
            {
                if (!string.Equals(target.TrackKey, current.TrackKey, StringComparison.Ordinal))
                {
                    var result = await assignTrack.ExecuteAsync(new AssignTeamTrackCommand(
                        competitionId,
                        teamId,
                        target.TrackKey,
                        user.UserId,
                        timeProvider.GetUtcNow()), transactionCt);
                    if (!result.Succeeded)
                    {
                        return Reject(Conflict(
                            result.ErrorMessage ?? "Team track was not updated."));
                    }
                }
                if (target.RegistrationStatus != current.RegistrationStatus)
                {
                    if (target.RegistrationStatus is not (
                        TeamRegistrationStatus.Approved or TeamRegistrationStatus.Rejected))
                    {
                        return Reject(Conflict(
                            "Moderators can only approve or reject team registration."));
                    }
                    var result = await reviewRegistration.ExecuteAsync(
                        competitionId,
                        teamId,
                        target.RegistrationStatus == TeamRegistrationStatus.Approved,
                        transactionCt);
                    if (!result.Succeeded)
                        return Reject(MapFailure(result.FailureCode, result.ErrorMessage));
                }
            }
            if ((sections & TeamPatchSection.Ban) != 0)
            {
                var result = await moderate.ExecuteAsync(new TeamModerationCommand(
                    competitionId,
                    teamId,
                    user.UserId,
                    target.IsBanned,
                    target.BanReason,
                    timeProvider.GetUtcNow(),
                    request.Ban!.AnnouncePublicly), transactionCt);
                if (!result.Succeeded)
                {
                    return Reject(Conflict(
                        result.ErrorMessage ?? "Team ban state was not updated."));
                }
            }

            var refreshed = await get.ExecuteAsync(
                competitionId,
                teamId,
                includePending: true,
                includeInternal: true,
                transactionCt);
            Results<Ok<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult> outcome =
                refreshed is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(TeamMapper.ToResponse(refreshed, links, HttpContext));
            return AtomicAggregatePatchDecision<Results<Ok<TeamResponse>, NotFound,
                ForbidHttpResult, ProblemHttpResult>>.Commit(outcome);
        }

        static AtomicAggregatePatchDecision<Results<Ok<TeamResponse>, NotFound,
            ForbidHttpResult, ProblemHttpResult>> Reject(
            Results<Ok<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult> failure) =>
            AtomicAggregatePatchDecision<Results<Ok<TeamResponse>, NotFound,
                ForbidHttpResult, ProblemHttpResult>>.Rollback(failure);
    }

    private static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Team was not updated.",
        detail: detail);

    private static TeamPatchSection ResolveSections(PatchTeamRequest request) =>
        (request.Profile is null ? TeamPatchSection.None : TeamPatchSection.Profile)
        | (request.Membership is null ? TeamPatchSection.None : TeamPatchSection.Membership)
        | (request.Registration is null ? TeamPatchSection.None : TeamPatchSection.Registration)
        | (request.Administration is null ? TeamPatchSection.None : TeamPatchSection.Administration)
        | (request.Ban is null ? TeamPatchSection.None : TeamPatchSection.Ban);

    private static Results<Ok<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>
        MapFailure(TeamRegistrationFailure? failure, string? detail) =>
        failure switch
        {
            TeamRegistrationFailure.TeamNotFound => TypedResults.NotFound(),
            _ => Conflict(detail ?? "Team was not updated.")
        };

    private static Results<Ok<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>
        MapMembershipFailure(TeamMembershipFailure? failure, string? detail) =>
        failure switch
        {
            TeamMembershipFailure.TeamNotFound or TeamMembershipFailure.MemberNotFound =>
                TypedResults.NotFound(),
            TeamMembershipFailure.TeamForbidden or TeamMembershipFailure.CaptainOnly =>
                TypedResults.Forbid(),
            _ => Conflict(detail ?? "Team membership was not updated.")
        };
}
