using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Permissions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionPermissionsRequest
{
    public Guid CompetitionId { get; set; }
    public IReadOnlyList<Guid> ManagerIds { get; set; } = [];
    public IReadOnlyList<Guid> JudgeIds { get; set; } = [];
    public IReadOnlyList<Guid> ObserverIds { get; set; } = [];
}

public sealed class UpdateCompetitionPermissionsValidator
    : Validator<UpdateCompetitionPermissionsRequest>
{
    public UpdateCompetitionPermissionsValidator()
    {
        RuleFor(request => request.ManagerIds).NotNull();
        RuleFor(request => request.JudgeIds).NotNull();
        RuleFor(request => request.ObserverIds).NotNull();
        RuleForEach(request => request.ManagerIds).NotEmpty();
        RuleForEach(request => request.JudgeIds).NotEmpty();
        RuleForEach(request => request.ObserverIds).NotEmpty();
    }
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionResourceManagerConflictCode>))]
public enum CompetitionResourceManagerConflictCode
{
    RolesOverlap,
    OwnerIncluded,
    UserNotFound,
    RoleNotEligible,
    EmailNotVerified
}

public sealed record CompetitionResourceManagerConflictResponse(
    CompetitionResourceManagerConflictCode Code,
    string Detail,
    IReadOnlyList<Guid> UserIds);

internal static class CompetitionResourceManagerConflictMapper
{
    public static CompetitionResourceManagerConflictResponse ToResponse(
        CompetitionResourceManagerConflictCode code,
        IReadOnlyList<Guid>? userIds) =>
        new(
            code,
            code switch
            {
                CompetitionResourceManagerConflictCode.RolesOverlap =>
                    "A collaborator cannot hold more than one competition role. Remove duplicate assignments and try again.",
                CompetitionResourceManagerConflictCode.OwnerIncluded =>
                    "The competition owner cannot also be listed as a manager, judge, or observer.",
                CompetitionResourceManagerConflictCode.UserNotFound =>
                    "One or more selected collaborator accounts no longer exist.",
                CompetitionResourceManagerConflictCode.RoleNotEligible =>
                    "One or more selected accounts do not have a platform role eligible for this competition role.",
                CompetitionResourceManagerConflictCode.EmailNotVerified =>
                    "One or more selected human accounts must verify their email before receiving this competition role.",
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
            },
            userIds ?? []);
}

internal static class UpdateCompetitionPermissionsMapper
{
    public static UpdateCompetitionPermissionsCommand ToCommand(
        UpdateCompetitionPermissionsRequest request,
        Guid actorId) =>
        new(
            request.CompetitionId,
            actorId,
            request.ManagerIds,
            request.JudgeIds,
            request.ObserverIds);
}

public sealed class UpdateCompetitionPermissionsEndpoint(
    UpdateCompetitionPermissions update,
    IUserContext user)
    : Endpoint<UpdateCompetitionPermissionsRequest,
        Results<
            NoContent,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionResourceManagerConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/permissions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionPermissions"));
        Summary(summary =>
        {
            summary.Summary = "Replaces competition permission assignments.";
            summary.Description = "Assigns mutually exclusive manager, judge, and observer user arrays.";
        });
    }

    public override async Task<
        Results<
            NoContent,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionResourceManagerConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        UpdateCompetitionPermissionsRequest request,
        CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await update.ExecuteAsync(
            UpdateCompetitionPermissionsMapper.ToCommand(request, user.UserId),
            ct);
        return result.State switch
        {
            CompetitionPermissionUpdateState.Updated => TypedResults.NoContent(),
            CompetitionPermissionUpdateState.NotFound => TypedResults.NotFound(),
            CompetitionPermissionUpdateState.Forbidden => TypedResults.Forbid(),
            CompetitionPermissionUpdateState.UserNotFound =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.UserNotFound,
                        result.UserIds)),
            CompetitionPermissionUpdateState.RoleNotEligible =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.RoleNotEligible,
                        result.UserIds)),
            CompetitionPermissionUpdateState.EmailNotVerified =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.EmailNotVerified,
                        result.UserIds)),
            CompetitionPermissionUpdateState.RolesOverlap =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.RolesOverlap,
                        result.UserIds)),
            CompetitionPermissionUpdateState.OwnerIncluded =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.OwnerIncluded,
                        result.UserIds)),
            _ => throw new InvalidOperationException(
                $"Unsupported competition permission state: {result.State}.")
        };
    }
}
