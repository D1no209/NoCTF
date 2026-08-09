using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameModeProtocol>))]
public enum GameModeProtocol
{
    Ctf,
    Awd,
    Awdp,
    Koh
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionStatusProtocol>))]
public enum CompetitionStatusProtocol
{
    Draft,
    Visible,
    Published,
    Running,
    Paused,
    Finished
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardVisibilityProtocol>))]
public enum LeaderboardVisibilityProtocol
{
    Normal,
    Frozen,
    Blackout
}

[Mapper]
public static partial class CompetitionProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameMode ToDomain(GameModeProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameModeProtocol ToProtocol(GameMode value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CompetitionStatusProtocol ToProtocol(CompetitionStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial LeaderboardVisibilityProtocol ToProtocol(
        CompetitionLeaderboardVisibility value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CompetitionLeaderboardVisibility ToDomain(
        LeaderboardVisibilityProtocol value);
}

public sealed record CompetitionResponse(
    Guid Id,
    string Title,
    string? Description,
    GameModeProtocol Mode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    CompetitionStatusProtocol Status,
    bool TeamRegistrationAutoApprove,
    bool AllowTeamRegistrationWhileRunning,
    int MaxTeamMembers,
    int MaxConcurrentRuntimeInstancesPerTeam,
    Guid OwnerId,
    LeaderboardVisibilityProtocol LeaderboardVisibility,
    DateTimeOffset? DeletedAt);

internal static class CompetitionMapper
{
    public static CompetitionResponse ToResponse(CompetitionView view) =>
        new(
            view.Id,
            view.Title,
            view.Description,
            CompetitionProtocolMapper.ToProtocol(view.Mode),
            view.StartTime,
            view.EndTime,
            CompetitionProtocolMapper.ToProtocol(view.Status),
            view.TeamRegistrationAutoApprove,
            view.AllowTeamRegistrationWhileRunning,
            view.MaxTeamMembers,
            view.MaxConcurrentRuntimeInstancesPerTeam,
            view.OwnerId,
            CompetitionProtocolMapper.ToProtocol(
                CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                    view.Status,
                    view.LeaderboardVisibility,
                    view.LeaderboardVisibilityStartsAt,
                    DateTimeOffset.UtcNow)),
            view.DeletedAt);
}

public sealed class GetCompetitionRequest { public Guid CompetitionId { get; set; } }

public sealed class GetCompetitionEndpoint(GetCompetition get)
    : Endpoint<GetCompetitionRequest, Results<Ok<CompetitionResponse>, NotFound>>
{
    public override void Configure() { Get("/competitions/{competitionId}"); AllowAnonymous(); }

    public override async Task<Results<Ok<CompetitionResponse>, NotFound>> ExecuteAsync(GetCompetitionRequest request, CancellationToken ct)
    {
        var view = await get.ExecuteAsync(Route<Guid>("competitionId"), false, ct);
        return view is null ? TypedResults.NotFound() : TypedResults.Ok(CompetitionMapper.ToResponse(view));
    }
}
