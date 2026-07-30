using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Permissions;
using Riok.Mapperly.Abstractions;
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
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<CompetitionResourceManagerConflictCode>))]
public enum CompetitionResourceManagerConflictCode
{
    RolesOverlap,
    OwnerIncluded,
    UserNotFound,
    RoleNotEligible
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

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class UpdateCompetitionPermissionsMapper
{
    public static partial UpdateCompetitionPermissionsCommand ToCommand(
        UpdateCompetitionPermissionsRequest request,
        Guid actorId);
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
