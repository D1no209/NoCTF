using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Media;

public static class LiveSoloMediaPolicy
{
    public static bool MayPrepare(LiveSoloMatch match) => Active(match.State)
        && match.Slots.Count == 2 && match.Slots.All(x => x.TeamId is not null && x.RosterLockedAt is not null);
    public static bool Active(LiveSoloMatchState state) => state is LiveSoloMatchState.Preparing or LiveSoloMatchState.Countdown
        or LiveSoloMatchState.Running or LiveSoloMatchState.Paused or LiveSoloMatchState.AwaitingAdjudication;
    public static LiveSoloMediaView View(LiveSoloMediaSession session, IReadOnlyDictionary<Guid, string> names) => new(session.Id, session.MatchId, session.Generation, session.State,
        session.ParticipantsMayViewOpponents, session.Participants.OrderBy(x => x.Side).ThenBy(x => x.UserId)
            .Select(x => new LiveSoloMediaMemberView(x.UserId, x.TeamId, x.Side, x.ScreenState, x.Identity, names.GetValueOrDefault(x.UserId) ?? string.Empty)).ToArray(),
        session.PublicDelaySeconds, session.RecordingEnabled);
}
