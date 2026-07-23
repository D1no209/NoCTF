using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Teams;

public sealed class CreateTeamRequest
{
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

public sealed record TeamResponse(
    Guid Id,
    Guid CompetitionId,
    string Name,
    string? AvatarUrl,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    TeamRegistrationStatus RegistrationStatus,
    bool IsLocked,
    DateTimeOffset RegisteredAt);

public sealed record TeamListResponse(IReadOnlyList<TeamResponse> Items);

public sealed class UpdateTeamRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

[Mapper]
internal static partial class TeamMapper
{
    public static partial CreateTeamCommand ToCommand(
        CreateTeamRequest request,
        Guid userId,
        DateTimeOffset registeredAt);
    public static partial TeamResponse ToResponse(TeamView view);
    public static partial UpdateTeamCommand ToCommand(UpdateTeamRequest request);
}

public sealed class CreateTeamEndpoint(CreateTeam create, IUserContext user)
    : Endpoint<CreateTeamRequest, Results<Created<TeamResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure() { Post("/competitions/{competitionId}/teams"); AuthSchemes("Bearer"); }
    public override async Task<Results<Created<TeamResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(CreateTeamRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await create.ExecuteAsync(TeamMapper.ToCommand(request, user.UserId, DateTimeOffset.UtcNow), ct);
        if (result.ErrorCode == "competition_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Team was not created.", detail: result.ErrorMessage);
        var response = TeamMapper.ToResponse(result.Value!);
        return TypedResults.Created($"/competitions/{request.CompetitionId}/teams/{response.Id}", response);
    }
}
