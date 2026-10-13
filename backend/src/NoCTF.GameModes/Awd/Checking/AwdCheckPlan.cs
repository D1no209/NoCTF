using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.GameModes.Awd.Checking;

public sealed record AwdCheckSample(long ActiveTicks, long DeadlineActiveTicks, bool Final);

/// <summary>Stable planned samples, independent of scheduler wakeups and runtime replacements.</summary>
public static class AwdCheckPlan
{
    public static IReadOnlyList<AwdCheckSample> Create(AwdRound round, AwdRoundRules rules,
        long enabledActiveTicks)
    {
        if (rules.CheckerIntervalSeconds <= 0 || rules.Options.FinalCheckBudgetSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(rules));
        var start = Math.Max(round.ActiveStartTicks, enabledActiveTicks);
        var end = round.ActiveEndTicks;
        if (start >= end) return [];
        var interval = TimeSpan.FromSeconds(rules.CheckerIntervalSeconds).Ticks;
        var budget = TimeSpan.FromSeconds(rules.Options.FinalCheckBudgetSeconds).Ticks;
        var finalStart = Math.Max(start, end - budget);
        var samples = new List<AwdCheckSample>();
        for (var at = start; at < finalStart; at = checked(at + interval))
            samples.Add(new(at, Math.Min(at + budget, finalStart), false));
        samples.Add(new(finalStart, end, !round.Partial));
        return samples;
    }

    public static Guid FactId(Guid roundId, Guid challengeId, Guid teamId, long activeTicks)
    {
        Span<byte> input = stackalloc byte[56];
        roundId.TryWriteBytes(input[..16]);
        challengeId.TryWriteBytes(input[16..32]);
        teamId.TryWriteBytes(input[32..48]);
        BinaryPrimitives.WriteInt64BigEndian(input[48..], activeTicks);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

    public static bool CanRetry(AwdFactDetails details, AwdRuleOptions options, DateTimeOffset now) =>
        details.Health == AwdHealth.InternalError
        && details.Execution == AwdCheckExecution.FailedToDetermine
        && details.Settlement == AwdSettlementState.Provisional
        && details.Attempt <= options.RetryCount
        && details.ReceivedAt is { } received
        && now >= received.AddSeconds(options.RetryIntervalSeconds)
        && details.Deadline is { } deadline && now < deadline;
}
