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
    private static readonly IReadOnlyDictionary<string, HashSet<string>> ApprovedProviderSql =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            // Bounded lateral evidence prefixes cannot be expressed by the mapped JSON model.
            ["backend/src/NoCTF.Infrastructure/GameplayFacts/AdjudicationPreview/HistoricalAdjudicationPreviewStore.cs"] = ["FromSqlInterpolated"],
            // Parameterized JSONB aggregation returns at most one row per selected Runner.
            ["backend/src/NoCTF.Infrastructure/Runtime/Capacity/RedisRunnerCapacityDiagnostics.cs"] = [".SqlQuery<"],
            // Short allocation/recovery transactions share one advisory lock; no provider calls inside.
            ["backend/src/NoCTF.Infrastructure/Runtime/Capacity/RuntimeCapacityCriticalSection.cs"] = ["ExecuteSqlInterpolated", "pg_advisory_"],
            // JSONB ownership containment includes auxiliary allocations after the primary is released.
            ["backend/src/NoCTF.Runner/Composition/RunnerAvailabilityPublisher.cs"] = ["FromSqlInterpolated"],
            ["backend/src/NoCTF.Runner/Messages/RuntimeResourceReconciliationHandler.cs"] = ["FromSqlInterpolated"],
            // Lock the owning Runtime while updating its active allocation document.
            ["backend/src/NoCTF.Runner/Messages/AuxiliaryRuntimeCapacity.cs"] = ["FromSqlInterpolated", "FOR UPDATE"],
            // Parameterized settings/runtime SHARE locks fence only the bounded local gateway lease write.
            ["backend/src/NoCTF.Infrastructure/Runtime/PublicAccess/PublicGatewayLeaseGuard.cs"] = ["ExecuteSqlInterpolated"],
            // Competition-scoped shared admission lock, with a bound UUID parameter.
            ["backend/src/NoCTF.Infrastructure/Competitions/Participation/CompetitionParticipationLock.cs"] = ["ExecuteSqlInterpolated"],
            ["backend/src/NoCTF.Infrastructure/Commands/Idempotency/TransactionalRequestReplay.cs"] = ["ExecuteSqlInterpolated", "pg_advisory_"],
            ["backend/src/NoCTF.Infrastructure/Challenges/Questions/CompetitionQuestionStore.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FromSqlInterpolated",
                "pg_advisory_",
                "WITH RECURSIVE"
            ],
            ["backend/src/NoCTF.Infrastructure/Challenges/Attachments/ChallengeAttachmentStore.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Challenges/ChallengeTemplateCriticalSection.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Challenges/TeamChallengeCriticalSection.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Administration/ActiveHumanAdministratorMutationGuard.cs"] =
            [
                "ExecuteSqlInterpolated",
                "LOCK TABLE"
            ],
            ["backend/src/NoCTF.Infrastructure/Administration/ResourceManagerRoleGuard.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            // Serialize revocation facts for one administrator-issued JWT.
            ["backend/src/NoCTF.Infrastructure/Administration/PlatformUserTokenCriticalSection.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Authentication/PasswordResetStore.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Authentication/EmailVerificationStore.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Competitions/Administration/AdminCompetitionStore.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FromSqlInterpolated",
                "FOR UPDATE",
                "WITH RECURSIVE"
            ],
            // Lock the proven notification roots/members to serialize new FK references with deletion.
            ["backend/src/NoCTF.Infrastructure/Competitions/Administration/CompetitionNotificationDeletionScope.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Competitions/Management/CompetitionManagementStore.cs"] =
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
            ["backend/src/NoCTF.Infrastructure/Teams/CompetitionTeamMutationCriticalSection.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/GameplayFacts/Intake/SubmissionIntakeStore.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/GameplayFacts/Intake/SubmissionAttemptLock.cs"] =
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
            ["backend/src/NoCTF.Infrastructure/GameplayFacts/Processing/InternalResultStore.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/GameplayFacts/Processing/BloodRankCriticalSection.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Runtime/Instances/RuntimeInstanceStore.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Runtime/Instances/SharedRuntimeScopeLock.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Infrastructure/Runtime/Instances/TeamRuntimeQuota.cs"] =
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
            ],
            ["backend/src/NoCTF.Worker/Storage/FileCleanupOperations.cs"] =
            [
                "ExecuteSqlInterpolated",
                "FOR UPDATE"
            ],
            ["backend/src/NoCTF.Worker/GameplayFacts/AwdpRecoveryOperations.cs"] =
            [
                "FromSqlInterpolated",
                "FOR UPDATE"
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
