using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

internal static class AwdAttackReceiptReader
{
    internal static async Task<(AwdFactDetails? Receipt, GameplayFactAdmissionFailureCode? Failure)> ReadAsync(
        NoCtfDbContext db, GameplayFactAdmissionSnapshot scope, DateTimeOffset receivedAt, CancellationToken ct)
    {
        var configuration = (AwdCompetitionModeConfiguration)scope.CompetitionConfiguration;
        var deadline = await AwdCompetitionDeadline.ReadAsync(db, scope.CompetitionId, scope.StartAt,
            scope.EndAt, configuration, receivedAt, ct);
        if (receivedAt >= deadline.AcceptanceEndsAt)
            return (null, GameplayFactAdmissionFailureCode.CompetitionFinished);
        var inTail = receivedAt >= deadline.EndsAt;
        var runningTicks = deadline.Clock.ElapsedAt(inTail ? deadline.EndsAt : receivedAt).Ticks;
        var rounds = deadline.Rounds;
        var hardeningTicks = rounds.FirstOrDefault()?.ActiveStartTicks
            ?? TimeSpan.FromSeconds(configuration.HardeningDurationSeconds).Ticks;
        if (runningTicks < hardeningTicks)
            return (null, GameplayFactAdmissionFailureCode.AwdHardeningActive);
        long gaps = 0;
        foreach (var round in rounds)
        {
            var start = checked(round.ActiveStartTicks + gaps);
            var end = checked(round.ActiveEndTicks + gaps);
            if (runningTicks < start)
                return (null, GameplayFactAdmissionFailureCode.AwdRoundUnavailable);
            var followingStart = checked(end + TimeSpan.FromSeconds(round.GapSeconds).Ticks);
            if (runningTicks < end || inTail && (runningTicks < followingStart
                    || round == rounds.Last()))
                return round.Missed
                    ? (null, GameplayFactAdmissionFailureCode.AwdRoundUnavailable)
                    : (new AwdFactDetails
                    {
                        ReceivedRoundId = round.Id,
                        ReceivedRoundNumber = round.Number,
                        ReceivedActiveTicks = Math.Min(runningTicks - gaps, round.ActiveEndTicks),
                        Settlement = AwdSettlementState.Provisional
                    }, null);
            if (configuration.AwdOptions.TotalRounds is { } limit && round.Number >= limit)
                return (null, GameplayFactAdmissionFailureCode.CompetitionFinished);
            var gap = TimeSpan.FromSeconds(round.GapSeconds).Ticks;
            if (runningTicks < checked(end + gap))
                return (null, GameplayFactAdmissionFailureCode.AwdRoundGap);
            gaps = checked(gaps + gap);
        }
        // A delayed scheduler cannot invent the round identity used by accepted facts.
        return (null, GameplayFactAdmissionFailureCode.AwdRoundUnavailable);
    }
}
