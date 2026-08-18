using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.GameplayFacts.Awdp;

public sealed record AwdpAchievementActivationView(
    Guid GameplayFactId,
    int EffectiveRound,
    DateTimeOffset EffectiveAt);

public sealed record AwdpDefenseProgressView(
    Guid? RuntimeInstanceId,
    RuntimeState? RuntimeState,
    RuntimeFailureCode? RuntimeFailureCode,
    Guid? GameplayFactId,
    Guid? PatchUploadId,
    NoCTF.Domain.Gameplay.GameplayFactState? State,
    NoCTF.Domain.Gameplay.GameplayFactResult? Result,
    NoCTF.Domain.Gameplay.GameplayFactFailureCode? FailureCode,
    AwdpFixStage? Stage,
    DateTimeOffset? TargetCreatedAt,
    DateTimeOffset? TargetExpiresAt,
    DateTimeOffset? TargetStoppedAt,
    DateTimeOffset? UpdatedAt);

public sealed record AwdpParticipantStateView(
    int? CurrentRound,
    RuntimeInstanceView? AttackRuntime,
    GameplayFactStatusView? LatestBreakAttempt,
    AwdpAchievementActivationView? BreakActivation,
    AwdpDefenseProgressView Defense,
    AwdpAchievementActivationView? FixActivation);

public interface IAwdpParticipantStateReader
{
    Task<AwdpParticipantStateView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class GetAwdpParticipantState(IAwdpParticipantStateReader reader)
{
    public Task<AwdpParticipantStateView?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        reader.FindAsync(competitionId, competitionChallengeId, userId, now, cancellationToken);
}
