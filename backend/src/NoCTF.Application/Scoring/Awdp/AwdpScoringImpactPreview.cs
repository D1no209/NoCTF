namespace NoCTF.Application.Scoring.Awdp;

public sealed record AwdpScoringImpactTeam(
    Guid TeamId,
    string TeamName,
    long CurrentScore,
    long ContinuousScore,
    long Delta);

public sealed record AwdpScoringImpactCompetition(
    Guid CompetitionId,
    string CompetitionTitle,
    int CurrentSchemaVersion,
    IReadOnlyList<AwdpScoringImpactTeam> Teams);

public interface IAwdpScoringImpactPreviewStore
{
    Task<IReadOnlyList<AwdpScoringImpactCompetition>> PreviewAsync(
        int limit,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken);
}

public sealed class PreviewAwdpScoringImpact(IAwdpScoringImpactPreviewStore store)
{
    public Task<IReadOnlyList<AwdpScoringImpactCompetition>> ExecuteAsync(
        int limit,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken = default) =>
        store.PreviewAsync(limit, projectedAt, cancellationToken);
}
