using System.Security.Cryptography;
using System.Text;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Adjudication;

public static class LiveSoloCorrectionPolicy
{
    public static bool CanPropose(LiveSoloMatch match, LiveSoloCorrectionProposal proposal)
    {
        if (match.Slots.Count != 2 || match.Slots.Select(x => x.TeamId).Distinct().Count() != 2) return false;
        var side = match.Slots.SingleOrDefault(x => x.TeamId == proposal.WinnerTeamId)?.Side;
        return match.State == LiveSoloMatchState.Completed && match.SupersededAt is null && match.PendingCorrectionId is null
            && match.WinnerTeamId is not null && match.Slots.Count == 2 && match.Slots.All(x => x.TeamId is not null)
            && proposal.LeftWins >= 0 && proposal.RightWins >= 0
            && (side == LiveSoloSide.Left && proposal.LeftWins == match.RequiredWins && proposal.RightWins < match.RequiredWins
                || side == LiveSoloSide.Right && proposal.RightWins == match.RequiredWins && proposal.LeftWins < match.RequiredWins)
            && (match.WinnerTeamId != proposal.WinnerTeamId || match.LeftWins != proposal.LeftWins || match.RightWins != proposal.RightWins);
    }
    public static IReadOnlyList<LiveSoloMatch> Downstream(Guid sourceId, IReadOnlyList<LiveSoloMatch> graph)
    {
        var selected = new HashSet<Guid> { sourceId }; var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var match in graph.Where(x => x.SupersededAt is null && !selected.Contains(x.Id)))
                if (match.Slots.Any(x => x.SourceMatchId is Guid id && selected.Contains(id))) changed |= selected.Add(match.Id);
        }
        return graph.Where(x => x.Id != sourceId && selected.Contains(x.Id)).OrderBy(x => x.Lane).ThenBy(x => x.Stage).ThenBy(x => x.Position).ToArray();
    }
    public static Guid PreviewId(LiveSoloCorrectionProposal proposal, IEnumerable<LiveSoloMatch> matches)
    {
        var data = new StringBuilder().Append(proposal.CompetitionId).Append(proposal.MatchId).Append(proposal.WinnerTeamId)
            .Append(':').Append(proposal.LeftWins).Append(':').Append(proposal.RightWins);
        foreach (var match in matches.OrderBy(x => x.Id))
        {
            data.Append('|').Append(match.Id).Append(match.ConcurrencyStamp).Append(':').Append((short)match.State)
                .Append(match.PendingCorrectionId).Append(match.CurrentRoundId).Append(match.CurrentMediaSessionId);
            foreach (var slot in match.Slots.OrderBy(x => x.Side))
                data.Append(':').Append((short)slot.Side).Append((short)slot.Source).Append(slot.Seed)
                    .Append(slot.TeamId).Append(slot.SourceMatchId).Append(slot.Resolved);
        }
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToString())).AsSpan(0, 16));
    }
}
