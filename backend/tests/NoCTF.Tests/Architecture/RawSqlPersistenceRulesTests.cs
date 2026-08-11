namespace NoCTF.Tests.Architecture;

public sealed class RawSqlPersistenceRulesTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string[] ForbiddenTokens =
    [
        "ExecuteSqlRaw",
        "FromSqlRaw",
        ".SqlQuery<",
        "PostgresException",
        "PostgresErrorCodes"
    ];
    private static readonly string[] ProviderSpecificTokens =
    [
        "ExecuteSqlInterpolated",
        "FromSqlInterpolated",
        "pg_advisory_",
        "FOR UPDATE",
        "SKIP LOCKED",
        "ON CONFLICT",
        "WITH RECURSIVE"
    ];
    private static readonly IReadOnlyDictionary<string, HashSet<string>> ApprovedProviderSql =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["backend/src/NoCTF.Infrastructure/Challenges/Questions/CompetitionQuestionStore.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FromSqlInterpolated",
                "pg_advisory_",
                "WITH RECURSIVE"
            ],
            ["backend/src/NoCTF.Infrastructure/Competitions/Administration/AdminCompetitionStore.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Notifications/NotificationReader.cs"] =
            [
                "FromSqlInterpolated",
                "WITH RECURSIVE"
            ],
            ["backend/src/NoCTF.Infrastructure/Storage/ManagedFileUploadRegistry.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/GameplayFacts/Intake/SubmissionIntakeStore.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/GameplayFacts/Processing/SubmissionProcessor.cs"] =
            [
                "ExecuteSqlInterpolated",
                "pg_advisory_"
            ],
            ["backend/src/NoCTF.Infrastructure/GameplayFacts/Processing/AwdpFixExecutionFence.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Scoring/Leaderboard/FusionLeaderboardCache.cs"] =
            [
                "ExecuteSqlInterpolated",
                "pg_advisory_"
            ],
            ["backend/src/NoCTF.Worker/BackendMessageHandlers.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FromSqlInterpolated",
                "FOR UPDATE",
                "SKIP LOCKED"
            ]
        };

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
                .Where(token => source.Content.Contains(token, StringComparison.Ordinal)
                    && (!ApprovedProviderSql.TryGetValue(source.Path, out var approved)
                        || !approved.Contains(token)))
                .Select(token => $"{source.Path}: unapproved {token}")))
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
