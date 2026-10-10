using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloCorrectionPolicyTests
{
    [Test]
    public async Task Proposal_requires_a_completed_distinct_pair_and_a_valid_changed_winning_tally()
    {
        var (match,proposal)=Pair();await Assert.That(LiveSoloCorrectionPolicy.CanPropose(match,proposal)).IsTrue();
        foreach(var invalid in new[]{proposal with {WinnerTeamId=Guid.NewGuid()},proposal with {RightWins=3},proposal with {LeftWins=2},
            proposal with {LeftWins=-1},proposal with {WinnerTeamId=match.WinnerTeamId!.Value,LeftWins=2,RightWins=0}})
            await Assert.That(LiveSoloCorrectionPolicy.CanPropose(match,invalid)).IsFalse();
        match.PendingCorrectionId=Guid.NewGuid();await Assert.That(LiveSoloCorrectionPolicy.CanPropose(match,proposal)).IsFalse();
    }
    [Test]
    public async Task Descendants_follow_both_winner_and_loser_edges_but_exclude_retired_matches()
    {
        var (source,_)=Pair();var winner=new LiveSoloMatch {Id=Guid.NewGuid(),Slots=[new(){SourceMatchId=source.Id,Source=LiveSoloSlotSource.Winner}]};
        var loser=new LiveSoloMatch {Id=Guid.NewGuid(),Slots=[new(){SourceMatchId=source.Id,Source=LiveSoloSlotSource.Loser}]};
        var final=new LiveSoloMatch {Id=Guid.NewGuid(),Slots=[new(){SourceMatchId=winner.Id},new(){SourceMatchId=loser.Id}]};
        var retired=new LiveSoloMatch {Id=Guid.NewGuid(),SupersededAt=DateTimeOffset.UtcNow,Slots=[new(){SourceMatchId=source.Id}]};
        await Assert.That(LiveSoloCorrectionPolicy.Downstream(source.Id,[source,winner,loser,final,retired]).Select(x=>x.Id))
            .IsEquivalentTo(new[]{winner.Id,loser.Id,final.Id});
    }
    [Test]
    public async Task Preview_proof_changes_with_slots_revisions_and_the_requested_outcome()
    {
        var (match,proposal)=Pair();var before=LiveSoloCorrectionPolicy.PreviewId(proposal,[match]);
        match.Slots[0].Source=LiveSoloSlotSource.Loser;
        await Assert.That(LiveSoloCorrectionPolicy.PreviewId(proposal,[match])).IsNotEqualTo(before);
        await Assert.That(LiveSoloCorrectionPolicy.PreviewId(proposal with {RightWins=1},[match]))
            .IsNotEqualTo(LiveSoloCorrectionPolicy.PreviewId(proposal,[match]));
    }
    [Test]
    public async Task Ordinary_judge_actions_cannot_unfreeze_a_correction()
    {
        var (match,_)=Pair();match.State=LiveSoloMatchState.Preparing;match.PendingCorrectionId=Guid.NewGuid();
        var command=new AdjudicateLiveSoloMatch(match.CompetitionId,match.Id,Guid.NewGuid(),match.ConcurrencyStamp,null,null,null,
            LiveSoloJudgeAction.ForfeitMatch,match.Slots[0].TeamId,"bypass");
        await Assert.That(LiveSoloAdjudicationPolicy.CanApply(match,null,command)).IsEqualTo(LiveSoloFailure.NotReady);
    }
    private static (LiveSoloMatch,LiveSoloCorrectionProposal) Pair()
    {
        var left=Guid.NewGuid();var right=Guid.NewGuid();var match=new LiveSoloMatch {Id=Guid.NewGuid(),CompetitionId=Guid.NewGuid(),State=LiveSoloMatchState.Completed,
            RequiredWins=2,LeftWins=2,WinnerTeamId=left,Slots=[new(){Side=LiveSoloSide.Left,TeamId=left},new(){Side=LiveSoloSide.Right,TeamId=right}]};
        return (match,new(match.CompetitionId,match.Id,Guid.NewGuid(),right,0,2));
    }
}
