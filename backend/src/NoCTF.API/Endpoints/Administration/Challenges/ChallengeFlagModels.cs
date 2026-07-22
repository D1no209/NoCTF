namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class CreateChallengeFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? StageId { get; set; }
    public Guid? ChallengeInstanceId { get; set; }
    public string Flag { get; set; } = string.Empty;
    public DateTimeOffset? ValidStart { get; set; }
    public DateTimeOffset? ValidEnd { get; set; }
}

public sealed class UpdateChallengeFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid FlagId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? StageId { get; set; }
    public Guid? ChallengeInstanceId { get; set; }
    public string Flag { get; set; } = string.Empty;
    public DateTimeOffset? ValidStart { get; set; }
    public DateTimeOffset? ValidEnd { get; set; }
    public long ExpectedRowVersion { get; set; }
}

public sealed class DeleteChallengeFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid FlagId { get; set; }
    public long ExpectedRowVersion { get; set; }
}

public sealed class GetChallengeFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid FlagId { get; set; }
}

public sealed record ChallengeFlagSecretResponse(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    string Flag,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long RowVersion,
    Guid? StageId = null,
    Guid? ChallengeInstanceId = null)
{
    public override string ToString() =>
        $"{nameof(ChallengeFlagSecretResponse)} {{ Id = {Id}, CompetitionId = {CompetitionId}, CompetitionChallengeId = {CompetitionChallengeId}, TeamId = {TeamId}, Flag = [REDACTED] }}";
}

public sealed record ChallengeFlagSecretListResponse(IReadOnlyList<ChallengeFlagSecretResponse> Items);
