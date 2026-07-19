namespace NoCTF.Application.Maintenance;

public interface ICompetitionRebuildProcessor
{
    Task RebuildAsync(Guid competitionId, CancellationToken cancellationToken);
}
