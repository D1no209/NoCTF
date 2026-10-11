using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Application.Competitions.Events;

public static class CompetitionEventApplicability
{
    public static readonly CompetitionEventKind[] OrdinaryOnly = [
        CompetitionEventKind.ChallengePublished, CompetitionEventKind.ChallengeUnpublished,
        CompetitionEventKind.ChallengeCreated, CompetitionEventKind.ChallengeUpdated, CompetitionEventKind.ChallengeDeleted,
        CompetitionEventKind.ChallengeDescriptionUpdated, CompetitionEventKind.HintPublished,
        CompetitionEventKind.FirstBloodAwarded, CompetitionEventKind.SecondBloodAwarded, CompetitionEventKind.ThirdBloodAwarded,
        CompetitionEventKind.ScoringRecorded, CompetitionEventKind.HintUnlocked, CompetitionEventKind.ChallengeWriteUpUnlocked,
        CompetitionEventKind.AwdpBreakAttempted, CompetitionEventKind.AwdpFixAttempted,
        CompetitionEventKind.AwdpBreakResolved, CompetitionEventKind.AwdpFixResolved
    ];
    public static bool PubliclyApplicable(GameMode mode, CompetitionEventKind kind) => mode != GameMode.LiveSolo || !OrdinaryOnly.Contains(kind);
}

public interface ICompetitionEventApplicabilityReader
{
    Task<bool> CanPublishAsync(Guid competitionId, CompetitionEventKind kind, CancellationToken ct);
    Task<bool> HasOrdinaryScoreboardAsync(Guid competitionId, CancellationToken ct);
}
