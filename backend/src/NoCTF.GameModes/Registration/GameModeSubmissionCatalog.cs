using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public static class GameModeSubmissionCatalog
{
    private static readonly IReadOnlyDictionary<GameMode, IGameModeSubmissionEvaluator> Evaluators =
        new Dictionary<GameMode, IGameModeSubmissionEvaluator>
        {
            [GameMode.Ctf] = new Ctf.Submission.CtfSubmissionEvaluator(),
            [GameMode.Awd] = new Awd.Submission.AwdSubmissionEvaluator(),
            [GameMode.Awdp] = new Awdp.Submission.AwdpSubmissionEvaluator(),
            [GameMode.Koh] = new Koh.Submission.KohSubmissionEvaluator(),
            [GameMode.Penetration] = new Penetration.Submission.PenetrationSubmissionEvaluator()
        };

    public static IGameModeSubmissionEvaluator Get(GameMode mode) =>
        Evaluators.TryGetValue(mode, out var evaluator)
            ? evaluator
            : throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.");

    public static IGameModeFlagSubmissionEvaluator GetFlag(GameMode mode) =>
        Get(mode) as IGameModeFlagSubmissionEvaluator
        ?? throw new InvalidOperationException($"Game mode {mode} does not accept Flag submissions.");

    public static IGameModeFixSubmissionEvaluator GetFix(GameMode mode) =>
        Get(mode) as IGameModeFixSubmissionEvaluator
        ?? throw new InvalidOperationException($"Game mode {mode} does not accept Fix submissions.");
}
