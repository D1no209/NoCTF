using System.Security.Claims;
using System.Text.Json;
using FastEndpoints;
using NoCTF.Infrastructure;

namespace NoCTF.API;

public class AuditLogPostProcessor : IGlobalPostProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task PostProcessAsync(IPostProcessorContext ctx, CancellationToken ct)
    {
        var httpCtx = ctx.HttpContext;

        var db = httpCtx.RequestServices.GetRequiredService<ApplicationDbContext>();

        // Extract user info
        var user = httpCtx.User;
        Guid? userId = null;
        string? userName = null;

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
        if (userIdClaim is not null && Guid.TryParse(userIdClaim.Value, out var parsedId))
            userId = parsedId;

        userName = user.FindFirst(ClaimTypes.Name)?.Value
                   ?? user.FindFirst("name")?.Value
                   ?? user.Identity?.Name;

        var ipAddress = httpCtx.Connection.RemoteIpAddress?.ToString();
        var userAgent = Limit(httpCtx.Request.Headers.UserAgent.ToString(), 512);
        var method = httpCtx.Request.Method;
        var path = Limit(httpCtx.Request.Path.Value ?? string.Empty, 2_048);
        var action = $"{method} {path}";

        string? newValues = null;
        string? exception = null;
        string? entityType = null;
        string? entityId = null;

        try
        {
            if (ctx.Request is not null)
            {
                var reqJson = JsonSerializer.Serialize(ctx.Request, ctx.Request.GetType(), JsonOptions);
                newValues = LimitJson(AuditValueRedactor.RedactJson(reqJson), 16_384);
                (entityType, entityId) = ExtractEntityInfo(ctx.Request);
            }
        }
        catch { /* ignore serialization errors */ }

        if (ctx.ExceptionDispatchInfo is not null)
        {
            exception = $"{ctx.ExceptionDispatchInfo.SourceException.GetType().Name}: request_failed";
        }
        else
        {
            var statusCode = httpCtx.Response.StatusCode;
            if (statusCode >= 400)
                exception = $"HTTP {statusCode}";
        }

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserName = userName,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EndpointPath = path,
            HttpMethod = method,
            NewValues = newValues,
            Timestamp = DateTime.UtcNow,
            Exception = exception
        };

        // Endpoint work must already be committed before post-processing. Do
        // not let failed or forgotten tracked mutations hitch a ride on the
        // best-effort audit write.
        db.ChangeTracker.Clear();
        db.AuditLogs.Add(auditLog);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Never let audit logging break the response
        }
    }

    private static readonly (string Suffix, string EntityName)[] EntityPropertyMap =
    [
        ("CompetitionId", "Competition"),
        ("ChallengeId", "Challenge"),
        ("TeamId", "Team"),
        ("UserId", "User"),
        ("ContainerId", "Container"),
    ];

    private static (string? entityType, string? entityId) ExtractEntityInfo(object request)
    {
        try
        {
            var type = request.GetType();
            foreach (var (suffix, entityName) in EntityPropertyMap)
            {
                var prop = type.GetProperty(suffix,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop is null) continue;
                var value = prop.GetValue(request);
                if (value is null) continue;
                return (entityName, value.ToString());
            }
        }
        catch { /* best-effort */ }
        return (null, null);
    }

    private static string Limit(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string LimitJson(string value, int maxLength)
        => value.Length <= maxLength
            ? value
            : JsonSerializer.Serialize(new
            {
                truncated = true,
                preview = value[..Math.Max(0, maxLength - 128)]
            }, JsonOptions);
}
