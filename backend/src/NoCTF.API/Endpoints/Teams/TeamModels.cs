using NoCTF.Domain.Teams;

namespace NoCTF.API.Endpoints.Teams;

public sealed class CreateTeamRequest
{
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

public sealed record TeamResponse(Guid Id, Guid CompetitionId, string Name, string? AvatarUrl,
    Guid CaptainId, TeamRegistrationStatus RegistrationStatus, bool IsLocked, DateTimeOffset RegisteredAt);

public sealed record TeamListResponse(IReadOnlyList<TeamResponse> Items);
