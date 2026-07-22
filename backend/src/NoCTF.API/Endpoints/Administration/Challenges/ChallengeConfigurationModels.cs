using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeConfigurationRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public int ExpectedRevision { get; set; }
    public string Json { get; set; } = string.Empty;
}

public sealed record ChallengeConfigurationResponse(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    GameMode Mode,
    string Json,
    int Revision,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset UpdatedAt);
