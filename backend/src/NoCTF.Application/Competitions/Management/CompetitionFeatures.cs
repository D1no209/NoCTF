namespace NoCTF.Application.Competitions.Management;

public sealed record CompetitionFeatures(bool ProgressionEnabled, bool ProgressionMapVisible);

public interface ICompetitionFeatureReader
{
    Task<CompetitionFeatures> ReadAsync(Guid competitionId, CancellationToken ct);
}
