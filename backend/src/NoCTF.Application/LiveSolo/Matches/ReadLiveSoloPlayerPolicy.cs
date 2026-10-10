namespace NoCTF.Application.LiveSolo.Matches;

/// <summary>Only the policy a participant needs to prepare and understand sharing.</summary>
public sealed record LiveSoloPlayerPolicy(bool Enabled, int RequiredWins, int MaximumRosterMembers,
    int PublicDelaySeconds, bool ParticipantsMayViewOpponents, bool RecordingEnabled);
public interface ILiveSoloPlayerPolicyReader
{
    Task<LiveSoloPlayerPolicy?> ReadAsync(Guid competitionId, Guid actorId, CancellationToken ct);
}
