using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Common;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Intake;

public sealed class GetFlagAttemptState(
    IGameplayFactIntakeStore store,
    IGameplayFactAdmissionModePolicy modePolicy)
{
    public async Task<FlagAttemptState?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await store.LoadAdmissionAsync(
            competitionId, competitionChallengeId, userId, cancellationToken);
        if (snapshot is null)
            return null;
        var rules = modePolicy.GetRules(
            snapshot.Mode,
            snapshot.CompetitionConfigurationJson,
            snapshot.ChallengeConfigurationJson);
        var practice = snapshot.Mode == GameMode.Ctf
            && snapshot.CompetitionStatus == CompetitionStatus.Finished
            && snapshot.PracticeModeEnabled;
        var maximum = !practice && rules.MaxFlagAttempts is > 0
            ? rules.MaxFlagAttempts
            : null;
        return new(
            maximum,
            snapshot.AcceptedFlagAttempts,
            maximum is { } limit
                ? Math.Max(0, limit - snapshot.AcceptedFlagAttempts)
                : null,
            snapshot.Mode == GameMode.Ctf && snapshot.HasCorrectFlag);
    }
}

public sealed class SubmitFlag(IGameplayFactIntakeStore store, IGameplayFactAdmissionModePolicy modePolicy)
{
    public async Task<OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>> ExecuteAsync(
        FlagGameplayFactCommand command,
        CancellationToken cancellationToken = default)
    {
        var byteLength = Encoding.UTF8.GetByteCount(command.Flag);
        if (byteLength is < 1 or > 4096 || command.Flag.Contains('\0', StringComparison.Ordinal))
            return OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.FlagInvalid, "Flag must contain 1 to 4096 UTF-8 bytes and cannot contain NUL.");
        var replay = await store.FindFlagReplayAsync(command.CompetitionId, command.CompetitionChallengeId, command.UserId, [command.Flag], cancellationToken);
        if (replay is { Length: > 0 }) return Map(replay[0]);

        var snapshot = await store.LoadAdmissionAsync(
            command.CompetitionId, command.CompetitionChallengeId, command.UserId, cancellationToken);
        if (snapshot is null)
            return OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.GameplayFactScopeNotFound, "GameplayFact scope was not found.");

        var kind = snapshot.Mode == GameMode.Awdp ? GameplayFactKind.BreakAttempt : GameplayFactKind.FlagAttempt;
        var rules = modePolicy.GetRules(
            snapshot.Mode, snapshot.CompetitionConfigurationJson, snapshot.ChallengeConfigurationJson,
            snapshot.ChallengeDefinitionJson);
        var admission = GameplayFactAdmissionPolicy.Check(snapshot, kind, rules, command.OccurredAt);
        if (!admission.Succeeded)
            return OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                admission.FailureCode!.Value, admission.ErrorMessage!);

        var gameplayFactId = Guid.CreateVersion7(command.OccurredAt);
        var accepted = await store.TryAcceptFlagAsync(
            new(
                gameplayFactId,
                command.CompetitionId,
                snapshot.TeamId,
                command.CompetitionChallengeId,
                command.UserId,
                kind,
                command.Flag,
                SHA256.HashData(Encoding.UTF8.GetBytes(command.Flag)),
                command.OccurredAt),
            snapshot,
            rules.MaxFlagAttempts,
            cancellationToken);
        return Map(accepted);
    }

    public async Task<OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>> ExecuteBatchAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        IReadOnlyList<string> flags,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        if (flags.Count == 0)
            return OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.FlagInvalid, "At least one Flag is required.");
        var replay = await store.FindFlagReplayAsync(competitionId, competitionChallengeId, userId, flags, cancellationToken);
        if (replay is not null)
            return OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>.Success(
                replay.Select(item => new GameplayFactAccepted(item.GameplayFactId!.Value, item.OccurredAt!.Value)).ToArray());
        foreach (var flag in flags)
        {
            var byteLength = Encoding.UTF8.GetByteCount(flag);
            if (byteLength is < 1 or > 4096 || flag.Contains('\0', StringComparison.Ordinal))
                return OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>.Failure(
                    GameplayFactAdmissionFailureCode.FlagInvalid, "Every Flag must contain 1 to 4096 UTF-8 bytes and cannot contain NUL.");
        }

        var snapshot = await store.LoadAdmissionAsync(
            competitionId, competitionChallengeId, userId, cancellationToken);
        if (snapshot is null)
            return OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.GameplayFactScopeNotFound, "GameplayFact scope was not found.");
        if (flags.Count > 1 && snapshot.Mode != GameMode.Awd)
            return OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.FlagBatchNotSupported, "Only AWD accepts a Flag collection.");

        var kind = snapshot.Mode == GameMode.Awdp ? GameplayFactKind.BreakAttempt : GameplayFactKind.FlagAttempt;
        var rules = modePolicy.GetRules(
            snapshot.Mode, snapshot.CompetitionConfigurationJson, snapshot.ChallengeConfigurationJson,
            snapshot.ChallengeDefinitionJson);
        var admission = GameplayFactAdmissionPolicy.Check(snapshot, kind, rules, receivedAt);
        if (!admission.Succeeded)
            return OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>.Failure(
                admission.FailureCode!.Value, admission.ErrorMessage!);

        var received = flags.Select(flag => new FlagGameplayFactReceived(
            Guid.CreateVersion7(receivedAt),
            competitionId,
            snapshot.TeamId,
            competitionChallengeId,
            userId,
            kind,
            flag,
            SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            receivedAt)).ToArray();
        var results = await store.TryAcceptFlagsAsync(
            received, snapshot, rules.MaxFlagAttempts, cancellationToken);
        if (results.Any(result => result.State != GameplayFactAcceptanceState.Created))
        {
            var first = results.First(result => result.State != GameplayFactAcceptanceState.Created);
            var mapped = Map(first);
            return OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>.Failure(
                mapped.FailureCode!.Value, mapped.ErrorMessage!);
        }
        return OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>.Success(
            results.Select(result => new GameplayFactAccepted(
                result.GameplayFactId!.Value,
                result.OccurredAt!.Value)).ToArray());
    }

    private static OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode> Map(GameplayFactAcceptanceResult result) => result.State switch
    {
        GameplayFactAcceptanceState.Created =>
            OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Success(new(result.GameplayFactId!.Value, result.OccurredAt!.Value)),
        GameplayFactAcceptanceState.AchievementAlreadySucceeded =>
            OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.AchievementAlreadySucceeded,
                "This AWDP attack achievement has already succeeded."),
        GameplayFactAcceptanceState.AttemptsExhausted =>
            OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.AttemptsExhausted, "The maximum number of accepted attempts has been reached."),
        _ => OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
            GameplayFactAdmissionFailureCode.GameplayFactConcurrency, "The submission could not be accepted because its scope changed.")
    };
}
