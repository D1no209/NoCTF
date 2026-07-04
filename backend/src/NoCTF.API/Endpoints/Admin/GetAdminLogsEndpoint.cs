using FastEndpoints;
using NoCTF.API.Logging;
using NoCTF.API.SignalR;

namespace NoCTF.API.Endpoints.Admin;

public class GetAdminLogsEndpoint(LogBuffer logBuffer) : Endpoint<EmptyRequest, List<LogEntryDto>>
{
    public override void Configure()
    {
        Get("/api/admin/logs");
        Roles("Admin");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var entries = logBuffer.GetRecent(100);
        await SendAsync(entries.ToList(), cancellation: ct);
    }
}
