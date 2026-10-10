using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Challenges.Timing;

public sealed record AdvanceChallengeOpening(Guid CompetitionChallengeId, Guid TimingRevision);
public sealed record RecalculateChallengeTiming(Guid CompetitionChallengeId, Guid TimingRevision);
public enum ChallengeTimingFailure { NotFound, UnsupportedMode, InvalidOrder, PublishedFutureOpening, PreviewExpired }
public sealed record ChangeChallengeTiming(Guid CompetitionId, Guid CompetitionChallengeId, ChallengeTiming Timing,
    DateTimeOffset Now);
public interface IChallengeTimingStore
{
    Task<ChallengeTimingFailure?> ChangeAsync(ChangeChallengeTiming command, CancellationToken ct);
    Task OpenAsync(AdvanceChallengeOpening command, DateTimeOffset now, CancellationToken ct);
    Task RecalculateAsync(RecalculateChallengeTiming command, DateTimeOffset now, CancellationToken ct);
}
