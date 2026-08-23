using Microsoft.AspNetCore.SignalR;
using NoCTF.API.Endpoints.Administration.Platform;

namespace NoCTF.API.SignalR.Hubs;

public interface IPlatformLogHubClient
{
    [HubMethodName("platformLogReceived")]
    Task PlatformLogReceived(
        PlatformLogResponse notification,
        CancellationToken cancellationToken);
}
