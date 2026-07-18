using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.Notifications;

namespace NoCTF.Worker.Submissions;

public sealed class ProcessFixSubmissionHandler(
    ISubmissionProcessor processor,
    ISubmissionStatusReader statusReader,
    ISubmissionResultNotification notification)
{
    public Task Handle(ProcessFixSubmission message, CancellationToken cancellationToken) =>
        HandleAsync(message, cancellationToken);

    private async Task HandleAsync(ProcessFixSubmission message, CancellationToken cancellationToken)
    {
        await processor.ProcessFixAsync(message.CompetitionId, message.SubmissionId, cancellationToken);
        var result = await statusReader.FindAsync(
            message.CompetitionId, message.SubmissionId, message.UserId, cancellationToken)
            ?? throw new InvalidOperationException("Processed submission status was not found.");
        await notification.PublishAsync(
            new(message.UserId, result),
            cancellationToken);
    }
}
