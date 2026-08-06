using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Profiles;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Teams;

public sealed class CreateTeamRequest
{
    private string name = string.Empty;

    public string Name
    {
        get => name;
        set => name = value?.Trim() ?? string.Empty;
    }
    public string? AvatarUrl { get; set; }
}

public sealed class CreateTeamRequestValidator : Validator<CreateTeamRequest>
{
    public CreateTeamRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(128);
        RuleFor(request => request.AvatarUrl).MaximumLength(2048);
    }
}

public sealed record GlobalTeamResponse(
    Guid Id,
    string Name,
    string? AvatarUrl,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    string? InvitationToken,
    DateTimeOffset CreatedAt);

public sealed record GlobalTeamListResponse(IReadOnlyList<GlobalTeamResponse> Items);

public sealed record TeamResponse(
    Guid Id,
    Guid CompetitionId,
    string Name,
    string? AvatarUrl,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    TeamRegistrationStatus RegistrationStatus,
    bool IsLocked,
    bool IsBanned,
    DateTimeOffset RegisteredAt,
    Guid? TeamProfileId);

public sealed record TeamListResponse(IReadOnlyList<TeamResponse> Items);

[Mapper]
internal static partial class TeamMapper
{
    public static partial TeamResponse ToResponse(TeamView view);
}

internal static class GlobalTeamMapper
{
    public static GlobalTeamResponse ToResponse(TeamProfileView view, Guid viewerId) => new(
        view.Id,
        view.Name,
        view.AvatarUrl,
        view.CaptainId,
        view.MemberIds,
        view.CaptainId == viewerId ? view.InvitationToken : null,
        view.CreatedAt);
}

public sealed class CreateTeamEndpoint(CreateTeamProfile create, IUserContext user)
    : Endpoint<CreateTeamRequest, Results<Created<GlobalTeamResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/teams");
        AuthSchemes("Bearer");
    }

    public override async Task<Results<Created<GlobalTeamResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateTeamRequest request,
        CancellationToken cancellationToken)
    {
        var result = await create.ExecuteAsync(new(
            user.UserId,
            request.Name,
            request.AvatarUrl,
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Team was not created.",
                detail: result.ErrorMessage);
        }
        var response = GlobalTeamMapper.ToResponse(result.Value!, user.UserId);
        return TypedResults.Created($"/teams/{response.Id}", response);
    }
}
