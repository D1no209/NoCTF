namespace NoCTF.Tests.Architecture;

public sealed class RawSqlPersistenceRulesTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string[] ForbiddenTokens =
    [
        "ExecuteSqlRaw",
        "FromSqlRaw",
        "PostgresException",
        "PostgresErrorCodes"
    ];
    private static readonly string[] ProviderSpecificTokens =
    [
        ".SqlQuery<",
        "ExecuteSqlInterpolated",
        "FromSqlInterpolated",
        "pg_advisory_",
        "LOCK TABLE",
        "FOR UPDATE",
        "SKIP LOCKED",
        "ON CONFLICT",
        "WITH RECURSIVE"
    ];
    [Test]
    public async Task Provider_specific_sql_is_parameterized_and_explicitly_scoped()
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "backend", "src");
        var sources = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Select(path => new SourceFile(
                Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'),
                File.ReadAllText(path)))
            .ToArray();
        var violations = sources
            .SelectMany(source => ForbiddenTokens
                .Where(token => source.Content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{source.Path}: forbidden {token}"))
            .Concat(sources.SelectMany(source => ProviderSpecificTokens
                .Where(token => source.Content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{source.Path}: provider-specific {token}")))
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

    private sealed record SourceFile(string Path, string Content);
}
