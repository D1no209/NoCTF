using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Challenges.Capabilities;
using Wolverine.Attributes;

namespace NoCTF.Worker;

[NonTransactional]
public sealed class KohPollingHandler(ControlExecutionStore executions)
{
    public Task Handle(PollKohChallenge message, CancellationToken ct) => executions.AcceptAsync(message, ct);
}

[NonTransactional]
public sealed class ControlObservationMessageHandler(ControlExecutionStore executions)
{
    public Task Handle(FinalizeControlObservation message, CancellationToken ct) => executions.FinalizeAsync(message, ct);
    public Task Handle(RecoverControlObservations message, CancellationToken ct) => executions.RecoverAsync(ct);
}
