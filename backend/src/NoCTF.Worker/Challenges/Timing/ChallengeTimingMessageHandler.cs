using NoCTF.Application.Challenges.Timing;

namespace NoCTF.Worker.Challenges.Timing;

public sealed class ChallengeTimingMessageHandler(IChallengeTimingStore store, TimeProvider clock)
{
    public Task Handle(AdvanceChallengeOpening message, CancellationToken ct) => store.OpenAsync(message, clock.GetUtcNow(), ct);
    public Task Handle(RecalculateChallengeTiming message, CancellationToken ct) => store.RecalculateAsync(message, clock.GetUtcNow(), ct);
}
