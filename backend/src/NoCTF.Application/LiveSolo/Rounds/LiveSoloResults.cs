using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Rounds;

public static class LiveSoloResults
{
    public static void Confirm(LiveSoloRound round, LiveSoloMatch match, LiveSoloEndReason reason)
    {
        round.ResultRevision = checked(round.ResultRevision + 1);
        round.EndReason = reason;
        round.ResultLeftWins = match.LeftWins;
        round.ResultRightWins = match.RightWins;
    }

    public static void Confirm(LiveSoloMatch match, LiveSoloEndReason reason, DateTimeOffset now)
    {
        match.ResultRevision = checked(match.ResultRevision + 1);
        match.EndReason = reason;
        match.ResultLeftWins = match.LeftWins;
        match.ResultRightWins = match.RightWins;
        match.ResultUpdatedAt = now;
    }
}
