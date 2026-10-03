using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Challenges;

public sealed class CompetitionChallengeTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(CompetitionChallengeTags.MaximumNameLength)]
    public required string Name { get; set; }
    [MaxLength(CompetitionChallengeTags.MaximumNameLength)]
    public required string NormalizedName { get; set; }
    public int Position { get; set; }
}

public static class CompetitionChallengeTags
{
    public const int MaximumCount = 20;
    public const int MaximumNameLength = 40;

    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    public static bool TryNormalize(IReadOnlyList<string>? names, out IReadOnlyList<string> normalized)
    {
        var result = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names ?? [])
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength)
            {
                normalized = [];
                return false;
            }
            var trimmed = name.Trim();
            if (keys.Add(NormalizeName(trimmed))) result.Add(trimmed);
        }
        normalized = result;
        return result.Count <= MaximumCount;
    }

    public static void Replace(List<CompetitionChallengeTag> tags, IReadOnlyList<string> names)
    {
        var keys = names.Select(NormalizeName).ToHashSet(StringComparer.Ordinal);
        tags.RemoveAll(tag => !keys.Contains(tag.NormalizedName));
        for (var position = 0; position < names.Count; position++)
        {
            var name = names[position];
            var key = NormalizeName(name);
            var tag = tags.SingleOrDefault(item => item.NormalizedName == key);
            if (tag is null)
            {
                tag = new CompetitionChallengeTag { Name = name, NormalizedName = key };
                tags.Add(tag);
            }
            tag.Name = name;
            tag.Position = position;
        }
    }
}
