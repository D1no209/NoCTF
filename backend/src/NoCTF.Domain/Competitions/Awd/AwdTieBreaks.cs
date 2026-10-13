using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Competitions.Awd;

public enum AwdTieBreakField : short { AttackPoints, HealthyRounds, AttackCount, LastAttackAt, RegistrationTime }
public enum AwdSortDirection : short { Descending, Ascending }
public readonly record struct AwdTieBreakCriterion(AwdTieBreakField Field, AwdSortDirection Direction);

[Table("awd_competition_tie_breaks")]
public sealed class AwdCompetitionTieBreak
{
    public Guid CompetitionId { get; set; }
    public int Position { get; set; }
    public AwdTieBreakField Field { get; set; }
    public AwdSortDirection Direction { get; set; }
}

[Table("awd_round_tie_breaks")]
public sealed class AwdRoundTieBreak
{
    public Guid RoundId { get; set; }
    public int Position { get; set; }
    public AwdTieBreakField Field { get; set; }
    public AwdSortDirection Direction { get; set; }
}

public static class AwdTieBreakPolicy
{
    public static IReadOnlyList<AwdTieBreakCriterion> Default { get; } = Array.AsReadOnly<AwdTieBreakCriterion>([
        new(AwdTieBreakField.AttackPoints, AwdSortDirection.Descending),
        new(AwdTieBreakField.HealthyRounds, AwdSortDirection.Descending),
        new(AwdTieBreakField.AttackCount, AwdSortDirection.Descending),
        new(AwdTieBreakField.LastAttackAt, AwdSortDirection.Ascending),
        new(AwdTieBreakField.RegistrationTime, AwdSortDirection.Ascending)
    ]);

    public static IReadOnlyList<AwdTieBreakCriterion> Resolve(IEnumerable<AwdTieBreakCriterion> criteria)
    {
        var values = criteria.ToArray();
        return values.Length == 0 ? Default : values;
    }

    public static bool IsValid(IReadOnlyList<AwdTieBreakCriterion> criteria) => criteria.Count is >= 1 and <= 5
        && criteria.All(item => Enum.IsDefined(item.Field) && Enum.IsDefined(item.Direction))
        && criteria.Select(item => item.Field).Distinct().Count() == criteria.Count;
}
