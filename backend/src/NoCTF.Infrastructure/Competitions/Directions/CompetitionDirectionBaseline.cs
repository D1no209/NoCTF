using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions.Directions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Directions;

/// <summary>Preserves existing template labels while giving every competition its own catalog.</summary>
public static class CompetitionDirectionBaseline
{
    public static async Task InitializeAsync(NoCtfDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var competitions = await db.Competitions.IgnoreQueryFilters().IgnoreAutoIncludes()
            .Include(item => item.Directions).ToArrayAsync(ct);
        foreach (var competition in competitions)
            if (competition.Directions.Count == 0)
            {
                var defaults = CompetitionDirectionDefaults.Create(competition.Id);
                competition.Directions.AddRange(defaults);
                db.AddRange(defaults);
            }
        var pending = await db.CompetitionChallenges.IgnoreQueryFilters().AsSplitQuery()
            .Where(item => item.DirectionId == null).ToArrayAsync(ct);
        var templateIds = pending.Select(item => item.ChallengeId).Distinct().ToArray();
        var labels = await db.Challenges.IgnoreQueryFilters().IgnoreAutoIncludes()
            .Where(item => templateIds.Contains(item.Id)).Select(item => new { item.Id, item.Direction })
            .ToDictionaryAsync(item => item.Id, item => item.Direction, ct);
        foreach (var item in pending)
        {
            var competition = competitions.Single(value => value.Id == item.CompetitionId);
            var label = labels[item.ChallengeId];
            var canonical = CompetitionDirectionDefaults.CanonicalName(label);
            var direction = competition.Directions.OrderByDescending(value => value.TemplateDirection == canonical)
                .FirstOrDefault(value => (value.TemplateDirection ?? value.NormalizedName) == canonical);
            if (direction is null)
            {
                var name = string.IsNullOrWhiteSpace(label) ? "Misc" : label.Trim();
                direction = new CompetitionDirection { Id = Guid.NewGuid(), CompetitionId = competition.Id,
                    Name = name, NormalizedName = name.ToUpperInvariant(), TemplateDirection = canonical, Icon = "flag", Position = competition.Directions.Count };
                competition.Directions.Add(direction);
                db.Add(direction);
            }
            item.Direction = direction;
            item.DirectionId = direction.Id;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
