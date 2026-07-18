using NoCTF.Application.Submissions.Ports;
using Wolverine;

namespace NoCTF.Infrastructure.Messaging;

public sealed class WolverineSubmissionQueue(IMessageBus bus) : ISubmissionQueue
{
    public async Task EnqueueAsync(ProcessFlagSubmission message, CancellationToken cancellationToken) =>
        await bus.SendAsync(message);

    public async Task EnqueueAsync(ProcessFixSubmission message, CancellationToken cancellationToken) =>
        await bus.SendAsync(message);
}
