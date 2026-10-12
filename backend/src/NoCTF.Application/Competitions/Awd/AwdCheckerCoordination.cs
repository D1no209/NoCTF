using NoCTF.Application.Messaging;

namespace NoCTF.Application.Competitions.Awd;

public interface IAwdCheckerCoordinator
{
    Task<MessageExecutionOutcome> DispatchAsync(DispatchAwdCheckers message, CancellationToken cancellationToken);
    Task ExpireAsync(AwdCheckerCallbackMissing message, CancellationToken cancellationToken);
}
