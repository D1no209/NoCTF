using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Notifications;
using NoCTF.Application.Submissions.Ports;
using NoCTF.API.SignalR.Hubs;

namespace NoCTF.API.SignalR.Publishing;

public sealed class SignalRSubmissionResultPublisher(
    IHubContext<CompetitionHub> hub) : ISubmissionResultPublisher
{
    public Task PublishAsync(Guid userId, SubmissionStatusView result, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString()).SendAsync("submissionResult", result, cancellationToken);
}
