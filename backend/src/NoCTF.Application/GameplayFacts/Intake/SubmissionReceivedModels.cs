using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Intake;

public sealed record FlagGameplayFactReceived(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid UserId,
    GameplayFactKind Kind,
    string Value,
    byte[] ValueSha256,
    DateTimeOffset OccurredAt)
{
    public override string ToString() =>
        $"{nameof(FlagGameplayFactReceived)} {{ GameplayFactId = {GameplayFactId}, Flag = [REDACTED] }}";
}

public sealed record HintUnlockGameplayFactReceived(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid UserId,
    Guid HintId,
    DateTimeOffset OccurredAt);

public sealed record ManualAdjustmentGameplayFactReceived(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid UserId,
    int Delta,
    DateTimeOffset OccurredAt);
