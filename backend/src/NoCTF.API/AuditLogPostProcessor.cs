using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FastEndpoints;
using NoCTF.Infrastructure;

namespace NoCTF.API;

public class AuditLogPostProcessor : IGlobalPostProcessor
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "secret", "flag", "jwt"
    };

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
        var userAgent = httpCtx.Request.Headers.UserAgent.ToString();
        var method = httpCtx.Request.Method;
        var path = httpCtx.Request.Path.Value ?? string.Empty;
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
                newValues = RedactSensitiveFields(reqJson);
                (entityType, entityId) = ExtractEntityInfo(ctx.Request);
            }
        }
        catch { /* ignore serialization errors */ }

        if (ctx.ExceptionDispatchInfo is not null)
        {
            exception = ctx.ExceptionDispatchInfo.SourceException.Message;
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

    private static string RedactSensitiveFields(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                RedactObject(obj);
                return obj.ToJsonString();
            }
            return json;
        }
        catch
        {
            return json;
        }
    }

    private static void RedactObject(JsonObject obj)
    {
        var keys = obj.Select(kv => kv.Key).ToList();
        foreach (var key in keys)
        {
            if (SensitiveKeys.Any(sensitive => key.Contains(sensitive, StringComparison.OrdinalIgnoreCase)))
            {
                obj[key] = JsonValue.Create("[REDACTED]");
            }
            else if (obj[key] is JsonObject nested)
            {
                RedactObject(nested);
            }
            else if (obj[key] is JsonArray arr)
            {
                RedactArray(arr);
            }
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

    private static void RedactArray(JsonArray arr)
    {
        foreach (var item in arr)
        {
            if (item is JsonObject obj)
                RedactObject(obj);
        }
    }
}
