using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionConfigurationRequest
{
    public Guid CompetitionId { get; set; }
    public int ExpectedRevision { get; set; }
    public string Json { get; set; } = string.Empty;
}

public sealed record CompetitionConfigurationResponse(
    Guid CompetitionId,
    GameMode Mode,
    string Json,
    int Revision,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset UpdatedAt);
