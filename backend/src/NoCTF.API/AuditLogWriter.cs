using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NoCTF.Infrastructure;

namespace NoCTF.API;

public static class AuditLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Add(
        ApplicationDbContext db,
        HttpContext httpContext,
        string action,
        string? entityType = null,
        string? entityId = null,
        object? newValues = null,
        object? oldValues = null,
        string? diff = null,
        string? exception = null)
    {
        var user = httpContext.User;
        Guid? userId = null;
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
        if (userIdClaim is not null && Guid.TryParse(userIdClaim.Value, out var parsedId))
            userId = parsedId;

        var userName = user.FindFirst(ClaimTypes.Name)?.Value
                       ?? user.FindFirst("name")?.Value
                       ?? user.Identity?.Name;

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserName = userName,
            IpAddress = ClientIpAddress.Normalize(httpContext.Connection.RemoteIpAddress),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EndpointPath = httpContext.Request.Path.Value ?? string.Empty,
            HttpMethod = httpContext.Request.Method,
            OldValues = Serialize(oldValues),
            NewValues = Serialize(newValues),
            Diff = diff,
            Timestamp = DateTime.UtcNow,
            Exception = exception,
        });
    }

    public static void AddSystem(
        ApplicationDbContext db,
        string action,
        string? entityType = null,
        string? entityId = null,
        object? newValues = null,
        object? oldValues = null,
        string? diff = null,
        string? exception = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserName = "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EndpointPath = "system",
            HttpMethod = "SYSTEM",
            OldValues = Serialize(oldValues),
            NewValues = Serialize(newValues),
            Diff = diff,
            Timestamp = DateTime.UtcNow,
            Exception = exception,
        });
    }

    private static string? Serialize(object? value)
        => AuditValueRedactor.Serialize(value, JsonOptions);
}
