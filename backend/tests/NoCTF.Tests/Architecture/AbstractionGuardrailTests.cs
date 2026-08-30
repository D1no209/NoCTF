using System.Text.RegularExpressions;

namespace NoCTF.Tests.Architecture;

/// <summary>
/// Prevents accidental reintroduction of shallow, Java-style infrastructure
/// wrappers while leaving real application ports and adapters unconstrained.
/// </summary>
public sealed partial class AbstractionGuardrailTests
{
    private static readonly string BackendRoot = FindBackendRoot();

    [Test]
    public async Task Source_has_no_generic_repository_or_unit_of_work_templates()
    {
        var violations = EnumerateSourceFiles()
            .Where(path => GenericRepositoryRegex().IsMatch(File.ReadAllText(path))
                || UnitOfWorkRegex().IsMatch(File.ReadAllText(path))
                || ImplTypeRegex().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(BackendRoot, path))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Service_locator_usage_is_limited_to_composition_and_hosting_code()
    {
        var violations = EnumerateSourceFiles()
            .Where(path => ServiceLocatorRegex().IsMatch(File.ReadAllText(path)))
            .Where(path => !IsCompositionOrHost(path))
            .Select(path => Path.GetRelativePath(BackendRoot, path))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Application_contracts_do_not_expose_iqueryable()
    {
        var applicationRoot = Path.Combine(BackendRoot, "src", "NoCTF.Application");
        var violations = Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && PublicIQueryableRegex().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(BackendRoot, path))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task New_code_does_not_create_horizontal_common_model_files()
    {
        var violations = EnumerateSourceFiles()
            .Where(path => Path.GetFileName(path).Equals("CommonModels.cs", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Equals("Dtos.cs", StringComparison.OrdinalIgnoreCase)
                || (Path.GetFileName(path).Equals("Models.cs", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains($"{Path.DirectorySeparatorChar}Runtime{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}Scoring{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}GameplayFacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(BackendRoot, path))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    private static bool IsCompositionOrHost(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}Composition{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.EndsWith($"{Path.DirectorySeparatorChar}Program.cs", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}NoCTF.Hosting{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}NoCTF.Infrastructure{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}Hosting{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}Messages{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || Path.GetFileName(path).Equals("WorkerMessageTopologyStartupValidator.cs", StringComparison.Ordinal)
        || Path.GetFileName(path).Contains("Infrastructure", StringComparison.Ordinal)
        || Path.GetFileName(path).Contains("Agent", StringComparison.Ordinal)
        || Path.GetFileName(path).Contains("Collector", StringComparison.Ordinal);

    private static IEnumerable<string> EnumerateSourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(BackendRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string FindBackendRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src"))
                && File.Exists(Path.Combine(current.FullName, "NoCTF.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the backend repository root.");
    }

    [GeneratedRegex(@"\bI?GenericRepository\s*<|\bIRepository\s*<")]
    private static partial Regex GenericRepositoryRegex();

    [GeneratedRegex(@"\bIUnitOfWork\b|\bclass\s+UnitOfWork\b")]
    private static partial Regex UnitOfWorkRegex();

    [GeneratedRegex(@"\bclass\s+\w+Impl\b|\brecord\s+\w+Impl\b")]
    private static partial Regex ImplTypeRegex();

    [GeneratedRegex(@"\b(?:GetRequiredService|GetService)\s*<")]
    private static partial Regex ServiceLocatorRegex();

    [GeneratedRegex(@"\bpublic\b[^;{}]*(?:IQueryable|IAsyncEnumerable)\s*<")]
    private static partial Regex PublicIQueryableRegex();
}
