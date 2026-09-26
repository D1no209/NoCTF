namespace NoCTF.Application.Competitions.Progression;

public enum StartProgressionChallengeResult : short
{
    Started,
    NotFound,
    GraphChanged
}

public interface IProgressionChallengeStarter
{
    Task<StartProgressionChallengeResult> StartAsync(
        Guid competitionId, Guid competitionChallengeId, Guid userId,
        long? expectedRevision, DateTimeOffset now, CancellationToken ct);
}
