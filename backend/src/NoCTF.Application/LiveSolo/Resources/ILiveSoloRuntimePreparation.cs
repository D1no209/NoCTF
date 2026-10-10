using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.Application.LiveSolo.Resources;

public interface ILiveSoloRuntimePreparation
{
    Task<LiveSoloFailure?> PrepareAsync(Guid roundQuestionId, DateTimeOffset now, CancellationToken ct);
    Task StopRoundAsync(Guid roundId, DateTimeOffset now, CancellationToken ct);
}
