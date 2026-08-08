using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Teams;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamRegistrationStatusProtocol>))]
public enum TeamRegistrationStatusProtocol
{
    Pending,
    Approved,
    Rejected
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamBanAppealStatusProtocol>))]
public enum TeamBanAppealStatusProtocol
{
    Submitted,
    Upheld,
    Accepted
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamBanSourceProtocol>))]
public enum TeamBanSourceProtocol
{
    ManualModeration,
    CheatIncident
}

public sealed class CreateTeamRequest
{
    private string name = string.Empty;

    public Guid CompetitionId { get; set; }
    public string Name
    {
        get => name;
        set => name = value?.Trim() ?? string.Empty;
    }
}

public sealed record TeamResponse(
    Guid Id,
    Guid CompetitionId,
    string Name,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    TeamRegistrationStatusProtocol RegistrationStatus,
    bool IsLocked,
    bool IsBanned,
    DateTimeOffset RegisteredAt);

public sealed record TeamListResponse(IReadOnlyList<TeamResponse> Items);

public sealed class UpdateTeamRequest
{
    private string name = string.Empty;

    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public string Name
    {
        get => name;
        set => name = value?.Trim() ?? string.Empty;
    }
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

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial TeamRegistrationStatusProtocol ToProtocol(
        TeamRegistrationStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial TeamBanAppealStatusProtocol ToProtocol(
        TeamBanAppealStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial TeamBanSourceProtocol ToProtocol(TeamBanSource value);
}

public sealed class CreateTeamEndpoint(CreateTeam create, IUserContext user)
    : Endpoint<CreateTeamRequest, Results<Created<TeamResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure() { Post("/competitions/{competitionId}/teams"); AuthSchemes("Bearer"); }
    public override async Task<Results<Created<TeamResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(CreateTeamRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await create.ExecuteAsync(TeamMapper.ToCommand(request, user.UserId, DateTimeOffset.UtcNow), ct);
        if (result.FailureCode == TeamRegistrationFailure.CompetitionNotFound) return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Team was not created.", detail: result.ErrorMessage);
        var response = TeamMapper.ToResponse(result.Value!);
        return TypedResults.Created($"/competitions/{request.CompetitionId}/teams/{response.Id}", response);
    }
}
