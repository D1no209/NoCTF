using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Application.Theory.Papers;

public sealed record TheoryPaperScore(Guid PaperId, int Attempt, long Score, DateTimeOffset SubmittedAt);
public sealed record TheoryEffectiveScore(long Points, DateTimeOffset? AchievedAt, bool HasSubmission);

public static class TheoryPaperGrading
{
    public static TheoryAnswerResult Evaluate(TheoryQuestionRevision revision, IReadOnlyCollection<Guid> selected)
    {
        if (selected.Count == 0) return TheoryAnswerResult.Unanswered;
        var unique = selected.ToHashSet();
        if (unique.Count != selected.Count || unique.Any(id => revision.Options.All(option => option.Id != id)))
            throw new ArgumentException("Selections must be distinct options from the assigned question.", nameof(selected));
        return unique.SetEquals(revision.Options.Where(option => option.IsCorrect).Select(option => option.Id))
            ? TheoryAnswerResult.Correct : TheoryAnswerResult.Wrong;
    }

    public static long Points(TheoryPaper paper)
    {
        if (paper.State != TheoryPaperState.Submitted) return 0;
        if (paper.Items.Any(item => item.Result is null))
            throw new InvalidOperationException("A submitted paper must be graded in full.");
        return paper.Items.Aggregate(0L, (sum, item) => checked(sum
            + (item.Result is TheoryAnswerResult.Correct or TheoryAnswerResult.FullCredit ? item.Points : 0)));
    }

    public static TheoryEffectiveScore Aggregate(TheoryScoringMode mode, IReadOnlyList<TheoryPaperScore> papers)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (papers.Count == 0) return new(0, null, false);
        if (papers.Any(paper => paper.Score < 0 || paper.Attempt <= 0)
            || papers.Select(paper => paper.Attempt).Distinct().Count() != papers.Count)
            throw new ArgumentException("Only distinct valid submitted attempts may be aggregated.", nameof(papers));
        var selected = mode switch
        {
            TheoryScoringMode.Highest => papers.OrderByDescending(paper => paper.Score)
                .ThenBy(paper => paper.SubmittedAt).ThenBy(paper => paper.Attempt).First(),
            TheoryScoringMode.Latest => papers.MaxBy(paper => paper.Attempt)!,
            TheoryScoringMode.Cumulative => papers.MaxBy(paper => paper.SubmittedAt)!,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return new(mode == TheoryScoringMode.Cumulative
            ? papers.Aggregate(0L, (sum, paper) => checked(sum + paper.Score)) : selected.Score,
            selected.SubmittedAt, true);
    }
}
