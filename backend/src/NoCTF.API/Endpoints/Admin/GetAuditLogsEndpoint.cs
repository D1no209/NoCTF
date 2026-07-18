using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class GetAuditLogsRequest
{
    public string? UserName { get; set; }
    public string? Action { get; set; }
    public string? EntityType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class AuditLogDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string EndpointPath { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string? NewValues { get; set; }
    public string? OldValues { get; set; }
    public string? Diff { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Exception { get; set; }
}

public class GetAuditLogsResponse
{
    public List<AuditLogDto> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class GetAuditLogsEndpoint(ApplicationDbContext db) : Endpoint<GetAuditLogsRequest, GetAuditLogsResponse>
{
    public override void Configure()
    {
        Get("/api/admin/audit-logs");
        Roles("Admin");
    }

    public override async Task HandleAsync(GetAuditLogsRequest req, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.UserName))
            query = query.Where(l => l.UserName != null && l.UserName.Contains(req.UserName));

        if (!string.IsNullOrWhiteSpace(req.Action))
            query = query.Where(l => l.Action.Contains(req.Action));

        if (!string.IsNullOrWhiteSpace(req.EntityType))
            query = query.Where(l => l.EntityType != null && l.EntityType.Contains(req.EntityType));

        if (req.From.HasValue)
            query = query.Where(l => l.Timestamp >= req.From.Value);

        if (req.To.HasValue)
            query = query.Where(l => l.Timestamp <= req.To.Value);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 200);
        var offset = CalculateOffset(page, pageSize);

        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip(offset)
            .Take(pageSize)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                UserId = l.UserId,
                UserName = l.UserName,
                IpAddress = l.IpAddress,
                Action = l.Action,
                EntityType = l.EntityType,
                EndpointPath = l.EndpointPath,
                HttpMethod = l.HttpMethod,
                NewValues = l.NewValues,
                OldValues = l.OldValues,
                Diff = l.Diff,
                Timestamp = l.Timestamp,
                Exception = l.Exception
            })
            .ToListAsync(ct);

        foreach (var item in items)
            item.IpAddress = ClientIpAddress.Normalize(item.IpAddress);

        await SendAsync(new GetAuditLogsResponse
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        }, cancellation: ct);
    }

    internal static int CalculateOffset(int page, int pageSize)
        => (int)Math.Min(
            (long)(Math.Max(1, page) - 1) * Math.Max(1, pageSize),
            int.MaxValue);
}
