using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeConfigurationRequest
{
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public int ExpectedRevision { get; set; }
    public string Json { get; set; } = string.Empty;
}

public sealed record ChallengeConfigurationResponse(
    Guid CompetitionId,
    Guid ChallengeId,
    GameMode Mode,
    string Json,
    int Revision,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset UpdatedAt);
