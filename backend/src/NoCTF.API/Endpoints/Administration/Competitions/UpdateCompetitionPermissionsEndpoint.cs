using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Permissions;
using Riok.Mapperly.Abstractions;

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
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/permissions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionPermissions")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Replaces competition permission assignments.";
            summary.Description = "Assigns mutually exclusive manager, judge, and observer user arrays.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UpdateCompetitionPermissionsRequest request,
        CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await update.ExecuteAsync(
            UpdateCompetitionPermissionsMapper.ToCommand(request, user.UserId),
            ct);
        return result.ErrorCode switch
        {
            null => TypedResults.NoContent(),
            "competition_not_found" => TypedResults.NotFound(),
            "competition_forbidden" => TypedResults.Forbid(),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Competition permissions were not updated.",
                detail: result.ErrorMessage)
        };
    }
}
