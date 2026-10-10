using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Adjudication;

public sealed record LiveSoloCorrectionProposal(Guid CompetitionId, Guid MatchId, Guid ActorId, Guid WinnerTeamId, int LeftWins, int RightWins);
public sealed record LiveSoloCorrectionImpact(Guid MatchId, Guid ConcurrencyStamp, LiveSoloMatchState State, bool RequiresReplay,
    Guid? LeftTeamId, string? LeftTeamName, Guid? RightTeamId, string? RightTeamName, Guid? ReplacementMatchId);
public sealed record LiveSoloCorrectionPreview(Guid PreviewId, Guid MatchStamp, Guid PreviousWinnerTeamId,
    int PreviousLeftWins, int PreviousRightWins, IReadOnlyList<LiveSoloCorrectionImpact> Downstream);
public sealed record LiveSoloCorrectionView(Guid Id, Guid MatchId, Guid ConcurrencyStamp, LiveSoloCorrectionState State,
    Guid WinnerTeamId, int LeftWins, int RightWins, string Reason, DateTimeOffset CreatedAt,
    string? ResolutionReason, DateTimeOffset? ResolvedAt, IReadOnlyList<LiveSoloCorrectionImpact> Downstream,
    Guid PreviousWinnerTeamId, int PreviousLeftWins, int PreviousRightWins, string ActorName, string? ResolvedByName);
public sealed record LiveSoloCorrectionResult(LiveSoloCorrectionView? Correction, LiveSoloFailure? Failure = null);
public sealed record BeginLiveSoloCorrection(LiveSoloCorrectionProposal Proposal, Guid PreviewId, string Reason);
public sealed record LiveSoloReplayConsent(Guid MatchId, Guid ConcurrencyStamp);
public sealed record ResolveLiveSoloCorrection(Guid CompetitionId, Guid MatchId, Guid ActorId, Guid CorrectionId,
    Guid ExpectedStamp, bool Apply, string Reason, IReadOnlyList<LiveSoloReplayConsent> Replays);
public interface ILiveSoloResultCorrectionStore
{
    Task<LiveSoloCorrectionPreview?> PreviewAsync(LiveSoloCorrectionProposal proposal, CancellationToken ct);
    Task<IReadOnlyList<LiveSoloCorrectionView>?> ListCorrectionsAsync(Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct);
    Task<LiveSoloCorrectionView?> ReadCorrectionAsync(Guid competitionId, Guid matchId, Guid correctionId, Guid actorId, CancellationToken ct);
    Task<LiveSoloCorrectionResult> BeginCorrectionAsync(BeginLiveSoloCorrection command, CancellationToken ct);
    Task<LiveSoloCorrectionResult> ResolveCorrectionAsync(ResolveLiveSoloCorrection command, CancellationToken ct);
}
public sealed class ManageLiveSoloResultCorrections(ILiveSoloResultCorrectionStore store)
{
    public Task<LiveSoloCorrectionResult> BeginAsync(BeginLiveSoloCorrection command, CancellationToken ct) =>
        command.PreviewId == Guid.Empty || !Reason(command.Reason)
            ? Task.FromResult(new LiveSoloCorrectionResult(null, LiveSoloFailure.InvalidConfiguration))
            : store.BeginCorrectionAsync(command with { Reason = command.Reason.Trim() }, ct);
    public Task<LiveSoloCorrectionResult> ResolveAsync(ResolveLiveSoloCorrection command, CancellationToken ct) =>
        command.ExpectedStamp == Guid.Empty || !Reason(command.Reason) || command.Replays.Count > 4096
            || command.Replays.Any(x => x.MatchId == Guid.Empty || x.ConcurrencyStamp == Guid.Empty)
            || command.Replays.Select(x => x.MatchId).Distinct().Count() != command.Replays.Count
            || !command.Apply && command.Replays.Count != 0
            ? Task.FromResult(new LiveSoloCorrectionResult(null, LiveSoloFailure.InvalidConfiguration))
            : store.ResolveCorrectionAsync(command with { Reason = command.Reason.Trim() }, ct);
    private static bool Reason(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 4000;
}
