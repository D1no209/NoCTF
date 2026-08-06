using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Profiles;

namespace NoCTF.API.Endpoints.Teams;

public sealed class UpdateTeamRequest
{
    private string name = string.Empty;

    public Guid TeamId { get; set; }
    public string Name
    {
        get => name;
        set => name = value?.Trim() ?? string.Empty;
    }
    public string? AvatarUrl { get; set; }
}

public sealed class UpdateTeamRequestValidator : Validator<UpdateTeamRequest>
{
    public UpdateTeamRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(128);
        RuleFor(request => request.AvatarUrl).MaximumLength(2048);
    }
}

public sealed class UpdateTeamEndpoint(
    UpdateTeamProfile update,
    ITeamProfileStore store,
    IUserContext user)
    : Endpoint<UpdateTeamRequest,
        Results<Ok<GlobalTeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/teams/{teamId}");
        AuthSchemes("Bearer");
    }

    public override async Task<
        Results<Ok<GlobalTeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UpdateTeamRequest request,
        CancellationToken cancellationToken)
    {
        request.TeamId = Route<Guid>("teamId");
        if (!await store.CanManageAsync(user.UserId, request.TeamId, cancellationToken))
            return TypedResults.Forbid();
        var result = await update.ExecuteAsync(new(
            request.TeamId,
            request.Name,
            request.AvatarUrl), cancellationToken);
        if (result.ErrorCode == "team_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Team was not updated.",
                detail: result.ErrorMessage);
        }
        return TypedResults.Ok(GlobalTeamMapper.ToResponse(result.Value!, user.UserId));
    }
}
