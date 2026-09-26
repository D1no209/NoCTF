using NoCTF.Domain.Competitions.Progression;

namespace NoCTF.Application.Competitions.Progression;

public sealed record ProgressionNodeDraft(
    Guid Id,
    ProgressionNodeKind Kind,
    Guid ResourceId);

public sealed record ProgressionEdgeDraft(
    Guid Id,
    Guid SourceNodeId,
    Guid TargetNodeId,
    ProgressionPrerequisiteCondition Condition);

public sealed record CompetitionProgressionView(
    Guid CompetitionId,
    bool Enabled,
    bool ShowPlayerMap,
    Guid? ConcurrencyStamp,
    long Revision,
    IReadOnlyList<ProgressionNodeDraft> Nodes,
    IReadOnlyList<ProgressionEdgeDraft> Edges);

public sealed record SaveCompetitionProgressionCommand(
    Guid CompetitionId,
    Guid? ExpectedConcurrencyStamp,
    bool Enabled,
    bool ShowPlayerMap,
    IReadOnlyList<ProgressionNodeDraft> Nodes,
    IReadOnlyList<ProgressionEdgeDraft> Edges,
    DateTimeOffset Now);

public enum CompetitionProgressionSaveFailure : short
{
    CompetitionNotFound,
    UnsupportedGameMode,
    ConcurrencyConflict,
    InvalidGraph,
    ChallengeNotFound,
    BadgeNotFound
}

public sealed record CompetitionProgressionSaveResult(
    CompetitionProgressionView? Progression,
    CompetitionProgressionSaveFailure? Failure = null,
    ProgressionGraphFailure? GraphFailure = null);

public interface ICompetitionProgressionStore
{
    Task<CompetitionProgressionView?> ReadAsync(Guid competitionId, CancellationToken ct);
    Task<CompetitionProgressionSaveResult> SaveAsync(
        SaveCompetitionProgressionCommand command,
        CancellationToken ct);
}

public sealed class GetCompetitionProgression(ICompetitionProgressionStore store)
{
    public Task<CompetitionProgressionView?> ExecuteAsync(Guid competitionId, CancellationToken ct) =>
        store.ReadAsync(competitionId, ct);
}

public sealed class SaveCompetitionProgression(ICompetitionProgressionStore store)
{
    public Task<CompetitionProgressionSaveResult> ExecuteAsync(
        SaveCompetitionProgressionCommand command,
        CancellationToken ct) => store.SaveAsync(command, ct);
}
