namespace NoCTF.Application.GameplayFacts.PatchVerification;

public static class PatchVerificationExecutionBudget
{
    public const int DefaultPatchTimeoutSeconds = 60;
    public const int DefaultReadyTimeoutSeconds = 30;
    public const int MaximumPatchTimeoutSeconds = 300;
    public const int MaximumCheckerTimeoutSeconds = 1800;
    public const int ArchiveDownloadBudgetSeconds = 60;
    public const int CleanupBudgetSeconds = 60;
    public const int ResultPublicationBudgetSeconds = 30;
    public const int HandlerExecutionTimeoutSeconds = 2400;
    public const int JetStreamMaximumAckExtensionSeconds =
        HandlerExecutionTimeoutSeconds + 300;

    public static TimeSpan Calculate(int patchTimeoutSeconds, int checkerTimeoutSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(patchTimeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(checkerTimeoutSeconds);
        return TimeSpan.FromSeconds(
            ArchiveDownloadBudgetSeconds
            + patchTimeoutSeconds
            + checkerTimeoutSeconds
            + CleanupBudgetSeconds);
    }

    public static bool FitsHandlerTimeout(int patchTimeoutSeconds, int checkerTimeoutSeconds) =>
        Calculate(patchTimeoutSeconds, checkerTimeoutSeconds)
        < TimeSpan.FromSeconds(HandlerExecutionTimeoutSeconds);

    public static DateTimeOffset CalculateDeadline(
        DateTimeOffset uploadedAt,
        DateTimeOffset? runtimeExpiresAt,
        int patchTimeoutSeconds,
        int checkerTimeoutSeconds)
    {
        var executionDeadline = uploadedAt.Add(Calculate(
            patchTimeoutSeconds,
            checkerTimeoutSeconds));
        return runtimeExpiresAt is { } expiresAt && expiresAt < executionDeadline
            ? expiresAt
            : executionDeadline;
    }
}

public static class PatchVerificationCommandRules
{
    public const string EntrypointPlaceholder = "{entrypoint}";
    public const int MaximumArguments = 64;
    public const int MaximumArgumentLength = 4096;
}
