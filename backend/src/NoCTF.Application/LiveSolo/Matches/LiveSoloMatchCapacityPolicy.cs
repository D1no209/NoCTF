using System.Linq.Expressions;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Matches;

public static class LiveSoloMatchCapacityPolicy
{
    // A series keeps its slot between Rounds; pausing its first countdown also keeps the slot.
    public static readonly Expression<Func<LiveSoloMatch, bool>> OccupiesSlot = match =>
        match.State != LiveSoloMatchState.Completed && match.State != LiveSoloMatchState.Canceled
        && (match.State == LiveSoloMatchState.Countdown || match.State == LiveSoloMatchState.Running
            || match.State == LiveSoloMatchState.Paused || match.StartedAt != null);

    public static bool CanEnter(int occupiedByOtherMatches, int maximumMatches) =>
        maximumMatches > 0 && occupiedByOtherMatches < maximumMatches;
}
