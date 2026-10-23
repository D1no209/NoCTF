using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions.Scoring;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Scoring;

public enum CompetitionScorePresetFailure
{
    CompetitionNotFound, PresetNotFound, UnsupportedMode, InvalidName, InvalidCurve, NameConflict
}
public sealed record CompetitionScorePresetView(CompetitionScorePreset Preset, int ChallengeCount);
public sealed record CompetitionScorePresetsResult(IReadOnlyList<CompetitionScorePresetView>? Items = null,
    CompetitionScorePresetFailure? Failure = null);
public sealed record CompetitionScorePresetMutationResult(CompetitionScorePresetView? Item = null,
    int AffectedChallengeCount = 0, int ReplacedChallengeCount = 0,
    CompetitionScorePresetFailure? Failure = null, string? Detail = null);

public interface ICompetitionScorePresetStore
{
    Task<CompetitionScorePresetsResult> ListAsync(Guid competitionId, CancellationToken ct);
    Task<CompetitionScorePresetMutationResult> SaveAsync(Guid competitionId, Guid? presetId,
        string name, ScoreCurveValue? curve, DateTimeOffset now, CancellationToken ct);
    Task<CompetitionScorePresetMutationResult> DeleteAsync(Guid competitionId, Guid presetId,
        DateTimeOffset now, CancellationToken ct);
}

public sealed class ManageCompetitionScorePresets(ICompetitionScorePresetStore store)
{
    public Task<CompetitionScorePresetsResult> ListAsync(Guid competitionId, CancellationToken ct) => store.ListAsync(competitionId, ct);
    public Task<CompetitionScorePresetMutationResult> SaveAsync(Guid competitionId, Guid? presetId,
        string name, ScoreCurveValue? curve, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > CompetitionChallengeTags.MaximumNameLength)
            return Task.FromResult(new CompetitionScorePresetMutationResult(Failure: CompetitionScorePresetFailure.InvalidName));
        return store.SaveAsync(competitionId, presetId, name.Trim(), curve, now, ct);
    }
    public Task<CompetitionScorePresetMutationResult> DeleteAsync(Guid competitionId, Guid presetId,
        DateTimeOffset now, CancellationToken ct) => store.DeleteAsync(competitionId, presetId, now, ct);
}
