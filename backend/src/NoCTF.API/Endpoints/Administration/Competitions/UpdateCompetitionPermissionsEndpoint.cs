using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Permissions;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionPermissionsRequest
{
    public Guid CompetitionId { get; set; }
    public IReadOnlyList<Guid> ManagerIds { get; set; } = [];
    public IReadOnlyList<Guid> JudgeIds { get; set; } = [];
    public IReadOnlyList<Guid> ObserverIds { get; set; } = [];
    [Required]
    public int ExpectedPermissionRevision { get; set; } = -1;
}

public sealed class UpdateCompetitionPermissionsValidator
    : Validator<UpdateCompetitionPermissionsRequest>
{
    public UpdateCompetitionPermissionsValidator()
    {
        RuleFor(request => request.ManagerIds).NotNull();
        RuleFor(request => request.JudgeIds).NotNull();
        RuleFor(request => request.ObserverIds).NotNull();
        RuleFor(request => request.ExpectedPermissionRevision)
            .GreaterThanOrEqualTo(0)
            .OverridePropertyName(nameof(UpdateCompetitionPermissionsRequest.ExpectedPermissionRevision))
            .WithMessage("Expected permission revision is required and must be non-negative.");
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
    EmailNotVerified,
    RevisionConflict
}

public sealed record CompetitionResourceManagerConflictResponse(
    CompetitionResourceManagerConflictCode Code,
    IReadOnlyList<Guid> UserIds);

internal static class CompetitionResourceManagerConflictMapper
{
    public static CompetitionResourceManagerConflictResponse ToResponse(
        CompetitionResourceManagerConflictCode code,
        IReadOnlyList<Guid>? userIds) =>
        new(code, userIds ?? []);
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
            request.ObserverIds,
            request.ExpectedPermissionRevision);
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
            CompetitionPermissionUpdateState.RevisionConflict =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.RevisionConflict,
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
