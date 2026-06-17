using NoCTF.PluginBase;
using NoCTF.Plugins.AWD;

namespace NoCTF.Plugins.AWDP;

/// <summary>
/// AWDP game mode: extends AWD with a patch-defense phase.
/// Delegates all flag-submission and round-tick logic to AwdGameMode.
/// </summary>
public class AwdpGameMode(AwdGameMode awdGameMode) : IGameMode
{
    public GameModeType Type => GameModeType.Awdp;

    public Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default)
        => awdGameMode.InitializeAsync(context, cancellationToken);

    public Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default)
        => awdGameMode.OnRoundTickAsync(context, cancellationToken);

    public Task<SubmissionResult> ProcessSubmissionAsync(SubmissionContext context, CancellationToken cancellationToken = default)
        => awdGameMode.ProcessSubmissionAsync(context, cancellationToken);
}
