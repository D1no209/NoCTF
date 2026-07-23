using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class CreateCompetitionRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GameMode Mode { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public int MaxTeamMembers { get; set; } = 5;
}

public sealed record CompetitionResponse(
    Guid Id,
    string Title,
    string? Description,
    GameMode Mode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    CompetitionStatus Status,
    bool TeamRegistrationAutoApprove,
    int MaxTeamMembers,
    Guid OwnerId);

public sealed class UpdateCompetitionRequest
{
    public Guid CompetitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; }
    public int MaxTeamMembers { get; set; }
}

[Mapper]
internal static partial class CompetitionMapper
{
    public static partial CreateCompetitionCommand ToCommand(
        CreateCompetitionRequest request,
        Guid ownerId,
        DateTimeOffset createdAt);
    public static partial UpdateCompetitionCommand ToCommand(
        UpdateCompetitionRequest request,
        Guid actorId,
        DateTimeOffset updatedAt);
    public static partial CompetitionResponse ToResponse(CompetitionView view);
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
