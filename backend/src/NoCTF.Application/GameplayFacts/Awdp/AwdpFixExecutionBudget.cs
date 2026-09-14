namespace NoCTF.Application.GameplayFacts.Awdp;

using NoCTF.Application.GameplayFacts.PatchVerification;

public static class AwdpFixExecutionBudget
{
    public const int DefaultPatchTimeoutSeconds = PatchVerificationExecutionBudget.DefaultPatchTimeoutSeconds;
    public const int DefaultReadyTimeoutSeconds = PatchVerificationExecutionBudget.DefaultReadyTimeoutSeconds;
    public const int MaximumPatchTimeoutSeconds = PatchVerificationExecutionBudget.MaximumPatchTimeoutSeconds;
    public const int MaximumCheckerTimeoutSeconds = PatchVerificationExecutionBudget.MaximumCheckerTimeoutSeconds;
    public const int ArchiveDownloadBudgetSeconds = PatchVerificationExecutionBudget.ArchiveDownloadBudgetSeconds;
    public const int CleanupBudgetSeconds = PatchVerificationExecutionBudget.CleanupBudgetSeconds;
    public const int ResultPublicationBudgetSeconds = PatchVerificationExecutionBudget.ResultPublicationBudgetSeconds;

    // This remains message-specific. It is deliberately longer than the maximum
    // accepted download + Patch + Checker + cleanup execution budget.
    public const int HandlerExecutionTimeoutSeconds = PatchVerificationExecutionBudget.HandlerExecutionTimeoutSeconds;
    public const int JetStreamMaximumAckExtensionSeconds =
        PatchVerificationExecutionBudget.JetStreamMaximumAckExtensionSeconds;

    public static TimeSpan Calculate(int patchTimeoutSeconds, int checkerTimeoutSeconds)
        => PatchVerificationExecutionBudget.Calculate(patchTimeoutSeconds, checkerTimeoutSeconds);

    public static bool FitsHandlerTimeout(
        int patchTimeoutSeconds,
        int checkerTimeoutSeconds) =>
        PatchVerificationExecutionBudget.FitsHandlerTimeout(patchTimeoutSeconds, checkerTimeoutSeconds);

    public static DateTimeOffset CalculateDeadline(
        DateTimeOffset uploadedAt,
        DateTimeOffset? runtimeExpiresAt,
        int patchTimeoutSeconds,
        int checkerTimeoutSeconds)
        => PatchVerificationExecutionBudget.CalculateDeadline(
            uploadedAt,
            runtimeExpiresAt,
            patchTimeoutSeconds,
            checkerTimeoutSeconds);
}

public static class AwdpPatchCommandRules
{
    public const string EntrypointPlaceholder = PatchVerificationCommandRules.EntrypointPlaceholder;
    public const int MaximumArguments = PatchVerificationCommandRules.MaximumArguments;
    public const int MaximumArgumentLength = PatchVerificationCommandRules.MaximumArgumentLength;
}
