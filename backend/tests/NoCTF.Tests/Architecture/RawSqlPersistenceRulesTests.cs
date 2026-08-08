namespace NoCTF.Tests.Architecture;

public sealed class RawSqlPersistenceRulesTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string[] ForbiddenTokens =
    [
        "ExecuteSqlRaw",
        "ExecuteSqlInterpolated",
        "FromSqlRaw",
        "FromSqlInterpolated",
        ".SqlQuery<",
        "pg_advisory_",
        "FOR UPDATE",
        "SKIP LOCKED",
        "ON CONFLICT",
        "PostgresException",
        "PostgresErrorCodes"
    ];

    [Test]
    public async Task Production_persistence_uses_ef_core_without_handwritten_sql()
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "backend", "src");
        var violations = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .SelectMany(path => ForbiddenTokens
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(RepositoryRoot, path)}: {token}"))
            .Order()
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Could not locate the NoCTF repository root.");
        return directory.Parent!.FullName;
    }
}
