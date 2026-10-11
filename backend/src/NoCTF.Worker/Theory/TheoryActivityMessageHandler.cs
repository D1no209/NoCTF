using NoCTF.Application.Theory.Papers;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.Worker.Theory;

public sealed class TheoryActivityMessageHandler(ITheoryPaperStore papers, ITheoryReviewStore reviews, TimeProvider clock)
{
    public Task Handle(SweepTheoryActivity message, CancellationToken ct) => papers.SweepAsync(message.ActivityId, clock.GetUtcNow(), ct);
    public Task Handle(ApplyTheoryCorrection message, CancellationToken ct) => reviews.ApplyAsync(message, clock.GetUtcNow(), ct);
}
