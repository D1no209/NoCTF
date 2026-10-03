namespace NoCTF.Application.Competitions.Directions;

public sealed record CompetitionDirectionView(Guid Id, string Name, string Icon);
public enum CompetitionDirectionFailure { CompetitionNotFound, InvalidCatalog, InvalidIcon, DirectionInUse, NameConflict }
public sealed record CompetitionDirectionsResult(IReadOnlyList<CompetitionDirectionView>? Items, CompetitionDirectionFailure? Failure = null);

public interface ICompetitionDirectionStore
{
    Task<IReadOnlyList<CompetitionDirectionView>?> ListAsync(Guid competitionId, CancellationToken ct);
    Task<CompetitionDirectionsResult> SaveAsync(Guid competitionId, IReadOnlyList<CompetitionDirectionView> items, DateTimeOffset now, CancellationToken ct);
}

public static class LucideIconCatalog
{
    private static readonly HashSet<string> Names = Load();
    public static bool Contains(string value) => Names.Contains(value);
    private static HashSet<string> Load()
    {
        using var stream = typeof(LucideIconCatalog).Assembly.GetManifestResourceStream(
            "NoCTF.Application.Competitions.Directions.lucide-icons.txt")
            ?? throw new InvalidOperationException("Lucide icon catalog is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim()).ToHashSet(StringComparer.Ordinal);
    }
}

public sealed class ManageCompetitionDirections(ICompetitionDirectionStore store)
{
    public Task<IReadOnlyList<CompetitionDirectionView>?> ListAsync(Guid competitionId, CancellationToken ct) => store.ListAsync(competitionId, ct);
    public Task<CompetitionDirectionsResult> SaveAsync(Guid competitionId, IReadOnlyList<CompetitionDirectionView> items, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = items.Select(item => item with { Name = item.Name.Trim(), Icon = item.Icon.Trim().ToLowerInvariant() }).ToArray();
        if (normalized.Length is < 1 or > 64 || normalized.Any(item => item.Id == Guid.Empty || item.Name.Length is < 1 or > 96)
            || normalized.Select(item => item.Id).Distinct().Count() != normalized.Length
            || normalized.Select(item => item.Name.ToUpperInvariant()).Distinct().Count() != normalized.Length)
            return Task.FromResult(new CompetitionDirectionsResult(null, CompetitionDirectionFailure.InvalidCatalog));
        if (normalized.Any(item => !LucideIconCatalog.Contains(item.Icon)))
            return Task.FromResult(new CompetitionDirectionsResult(null, CompetitionDirectionFailure.InvalidIcon));
        return store.SaveAsync(competitionId, normalized, now, ct);
    }
}
