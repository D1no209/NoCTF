using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class AddCompetitionCollaboratorRequest
{
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public CompetitionCollaboratorRole Role { get; set; }
}

public sealed record CompetitionCollaboratorResponse(Guid UserId, CompetitionCollaboratorRole Role, DateTimeOffset AddedAt);
public sealed record CompetitionCollaboratorListResponse(IReadOnlyList<CompetitionCollaboratorResponse> Items);
