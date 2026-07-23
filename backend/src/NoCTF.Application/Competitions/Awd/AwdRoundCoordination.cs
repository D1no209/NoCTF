using NoCTF.Application.Messaging;

namespace NoCTF.Application.Competitions.Awd;

public interface IAwdRoundCoordinator
{
    Task<MessageExecutionOutcome> AdvanceAsync(
        AdvanceAwdRound message,
        CancellationToken cancellationToken);

    Task<MessageExecutionOutcome> GenerateFlagsAsync(
        GenerateAwdFlags message,
        CancellationToken cancellationToken);
}
