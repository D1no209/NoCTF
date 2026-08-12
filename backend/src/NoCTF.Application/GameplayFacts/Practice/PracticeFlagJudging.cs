namespace NoCTF.Application.GameplayFacts.Practice;

public enum PracticeFlagJudgement
{
    Correct,
    Wrong
}

public enum PracticeFlagFailureCode
{
    PracticeUnavailable,
    TeamNotEligible,
    RuntimeNotRunning,
    FlagInvalid
}

public sealed record JudgePracticeFlagCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid UserId,
    string Flag,
    DateTimeOffset SubmittedAt);

public sealed record PracticeFlagResult(
    PracticeFlagJudgement? Judgement = null,
    PracticeFlagFailureCode? FailureCode = null);

public interface IPracticeFlagJudge
{
    Task<PracticeFlagResult> JudgeAsync(
        JudgePracticeFlagCommand command,
        CancellationToken cancellationToken);
}

public sealed class JudgePracticeFlag(IPracticeFlagJudge judge)
{
    public Task<PracticeFlagResult> ExecuteAsync(
        JudgePracticeFlagCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(command.Flag)
            || command.Flag.IndexOf('\0', StringComparison.Ordinal) >= 0
            || System.Text.Encoding.UTF8.GetByteCount(command.Flag) > 4_096)
        {
            return Task.FromResult(new PracticeFlagResult(
                FailureCode: PracticeFlagFailureCode.FlagInvalid));
        }

        return judge.JudgeAsync(command, ct);
    }
}
