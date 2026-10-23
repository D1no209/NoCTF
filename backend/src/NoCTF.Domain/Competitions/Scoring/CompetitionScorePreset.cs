using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions.Scoring;

public sealed class CompetitionScorePreset : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [MaxLength(CompetitionChallengeTags.MaximumNameLength)]
    public required string Name { get; set; }
    [MaxLength(CompetitionChallengeTags.MaximumNameLength)]
    public required string NormalizedName { get; set; }
    public ScoreCurveValue? ScoreCurve { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static CompetitionScorePreset? Match(IEnumerable<string> tags, IReadOnlyCollection<CompetitionScorePreset> presets)
    {
        var names = tags.Select(CompetitionChallengeTags.NormalizeName).ToHashSet(StringComparer.Ordinal);
        return presets.SingleOrDefault(preset => preset.ScoreCurve is not null && names.Contains(preset.NormalizedName));
    }
}

public enum CtfScoreCurveSource { CompetitionDefault, TagPreset, ChallengeOverride }
public sealed record EffectiveCtfScoring(CtfScoreCurveSource Source, ScoreCurveValue ScoreCurve);

public static class CtfScoreCurveResolver
{
    public static CompetitionChallengeRules ForValidation(CompetitionChallengeRules rules, CompetitionScorePreset? preset) =>
        rules is CtfCompetitionChallengeRules { HasScoreCurve: false } ctf && preset?.ScoreCurve is not null
            ? ctf.WithInheritedScoreCurve(preset) : rules;

    public static ScoreCurveValue? Override(CtfCompetitionChallengeRules? rules, CompetitionScorePreset? preset) =>
        rules?.HasScoreCurve == true ? rules.ScoreCurve : preset?.ScoreCurve;

    public static EffectiveCtfScoring Resolve(CtfCompetitionChallengeRules rules,
        CompetitionScorePreset? preset, CtfCompetitionModeConfiguration competition) => new(
            rules.HasScoreCurve ? CtfScoreCurveSource.ChallengeOverride
                : preset?.ScoreCurve is not null ? CtfScoreCurveSource.TagPreset : CtfScoreCurveSource.CompetitionDefault,
            Override(rules, preset) ?? competition.DefaultScoreCurve);
}
