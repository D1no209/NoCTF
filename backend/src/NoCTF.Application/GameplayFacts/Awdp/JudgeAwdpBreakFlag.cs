namespace NoCTF.Application.GameplayFacts.Awdp;

public enum AwdpBreakFlagJudgement
{
    Correct,
    Wrong
}

public enum AwdpBreakFlagJudgementFailureCode
{
    JudgementUnavailable,
    TeamNotEligible,
    AchievementNotSucceeded,
    FlagInvalid
}

public sealed record JudgeAwdpBreakFlagCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid UserId,
    string Flag);

public sealed record AwdpBreakFlagJudgementResult(
    AwdpBreakFlagJudgement? Judgement = null,
    AwdpBreakFlagJudgementFailureCode? FailureCode = null);

public interface IAwdpBreakFlagJudge
{
    Task<AwdpBreakFlagJudgementResult> JudgeAsync(
        JudgeAwdpBreakFlagCommand command,
        CancellationToken cancellationToken);
}

public sealed class JudgeAwdpBreakFlag(IAwdpBreakFlagJudge judge)
{
    public Task<AwdpBreakFlagJudgementResult> ExecuteAsync(
        JudgeAwdpBreakFlagCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(command.Flag)
            || command.Flag.IndexOf('\0', StringComparison.Ordinal) >= 0
            || System.Text.Encoding.UTF8.GetByteCount(command.Flag) > 4_096)
        {
            return Task.FromResult(new AwdpBreakFlagJudgementResult(
                FailureCode: AwdpBreakFlagJudgementFailureCode.FlagInvalid));
        }

        return judge.JudgeAsync(command, ct);
    }
}
