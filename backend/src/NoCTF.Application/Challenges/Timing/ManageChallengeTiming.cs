using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Challenges.Timing;

public sealed record AdvanceChallengeOpening(Guid CompetitionChallengeId, Guid TimingRevision);
public sealed record RecalculateChallengeTiming(Guid CompetitionChallengeId, Guid TimingRevision);
public enum ChallengeTimingFailure { NotFound, UnsupportedMode, InvalidOrder, PublishedFutureOpening, PreviewExpired, PreviewRequired, SeparateTimingChange }
public sealed record ChangeChallengeTiming(Guid CompetitionId, Guid CompetitionChallengeId, ChallengeTiming Timing,
    DateTimeOffset Now);
public interface IChallengeTimingStore
{
    Task<ChallengeTimingFailure?> ChangeAsync(ChangeChallengeTiming command, CancellationToken ct);
    Task OpenAsync(AdvanceChallengeOpening command, DateTimeOffset now, CancellationToken ct);
    Task RecalculateAsync(RecalculateChallengeTiming command, DateTimeOffset now, CancellationToken ct);
}

public sealed record ChallengeTimingProjectionOverride(Guid CompetitionChallengeId, ChallengeTiming Timing);
public sealed record ChallengeTimingImpact(Guid TeamId, string TeamName, long ScoreBefore, long ScoreAfter,
    bool BloodChanged, int CompletionBefore, int CompletionAfter, int ProgressionNodesChanged = 0);
public sealed record ChallengeTimingPreview(string Token, DateTimeOffset ExpiresAt, int AffectedAttempts,
    IReadOnlyList<ChallengeTimingImpact> Teams);
public interface IChallengeTimingPreviewReader
{
    Task<ChallengeTimingFailure?> ValidateCandidateAsync(ChangeChallengeTiming candidate, CancellationToken ct);
    Task<ChallengeTimingPreview?> PreviewAsync(ChangeChallengeTiming candidate, Guid actorId, CancellationToken ct);
    Task<bool> ValidateAsync(ChangeChallengeTiming candidate, Guid actorId, string? token, CancellationToken ct);
}
public sealed class ManageChallengeTiming(IChallengeTimingStore store, IChallengeTimingPreviewReader previews)
{
    public Task<ChallengeTimingFailure?> ValidateCandidateAsync(ChangeChallengeTiming candidate, CancellationToken ct) =>
        previews.ValidateCandidateAsync(candidate, ct);
    public Task<ChallengeTimingPreview?> PreviewAsync(ChangeChallengeTiming candidate, Guid actorId, CancellationToken ct) =>
        previews.PreviewAsync(candidate, actorId, ct);
    public async Task<ChallengeTimingFailure?> ChangeAsync(ChangeChallengeTiming candidate, Guid actorId, string? token, CancellationToken ct)
    {
        if (!candidate.Timing.IsValid) return ChallengeTimingFailure.InvalidOrder;
        if (!await previews.ValidateAsync(candidate, actorId, token, ct)) return ChallengeTimingFailure.PreviewExpired;
        return await store.ChangeAsync(candidate, ct);
    }
    public async Task<ChallengeTimingFailure?> ValidateAsync(ChangeChallengeTiming candidate, Guid actorId, string? token, CancellationToken ct)
    {
        if (!candidate.Timing.IsValid) return ChallengeTimingFailure.InvalidOrder;
        return await previews.ValidateAsync(candidate, actorId, token, ct) ? null : ChallengeTimingFailure.PreviewExpired;
    }
    public Task<ChallengeTimingFailure?> ApplyValidatedAsync(ChangeChallengeTiming candidate, CancellationToken ct) => store.ChangeAsync(candidate, ct);
}
