using System.Text.Json;
using FastEndpoints;
using NoCTF.Application.QqBot;

namespace NoCTF.API.Endpoints;

internal static class QqBotAgentEndpointRuntime
{
    private const int MaxBodyBytes = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<(QqBotAgentAuthenticationResult Auth, T? Request)> ReadAsync<T>(
        HttpContext context,
        IQqBotAgentService service,
        CancellationToken ct)
    {
        if (context.Request.ContentLength is > MaxBodyBytes)
            return (QqBotAgentAuthenticationResult.Failure("request_too_large"), default);
        await using var stream = new MemoryStream();
        await context.Request.Body.CopyToAsync(stream, ct);
        if (stream.Length > MaxBodyBytes)
            return (QqBotAgentAuthenticationResult.Failure("request_too_large"), default);
        var bytes = stream.ToArray();
        var auth = await service.AuthenticateAsync(context, bytes, ct);
        if (!auth.Succeeded) return (auth, default);
        try { return (auth, JsonSerializer.Deserialize<T>(bytes, JsonOptions)); }
        catch (JsonException) { return (QqBotAgentAuthenticationResult.Failure("request_json_invalid"), default); }
    }
}

public abstract class QqBotAgentEndpoint<TRequest, TResponse>(IQqBotAgentService service)
    : EndpointWithoutRequest<TResponse>
    where TRequest : notnull
{
    protected IQqBotAgentService Service { get; } = service;
    public override void Configure()
    {
        AllowAnonymous();
        Description(builder => builder.Accepts<TRequest>("application/json"));
        Options(builder => builder.RequireRateLimiting("qqbot-agent"));
    }
    protected async Task<(Guid AgentId, TRequest? Request)> AuthenticateAsync(CancellationToken ct)
    {
        var result = await QqBotAgentEndpointRuntime.ReadAsync<TRequest>(HttpContext, Service, ct);
        if (!result.Auth.Succeeded || result.Request is null)
        {
            await SendAsync(default!, result.Auth.ErrorCode == "signature_replay" ? 409 : 401, ct);
            return default;
        }
        return (result.Auth.AgentId, result.Request);
    }
}

public sealed class QqBotAgentHeartbeatEndpoint(IQqBotAgentService service)
    : QqBotAgentEndpoint<QqBotHeartbeatRequest, QqBotAgentOperationResult>(service)
{
    public override void Configure() { base.Configure(); Post("/api/integrations/qqbot/v1/heartbeat"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var (agentId, request) = await AuthenticateAsync(ct); if (request is null) return;
        await SendAsync(await Service.HeartbeatAsync(agentId, request, ct), cancellation: ct);
    }
}

public sealed class QqBotAgentGroupSyncEndpoint(IQqBotAgentService service)
    : QqBotAgentEndpoint<QqBotGroupSyncRequest, QqBotGroupSyncResult>(service)
{
    public override void Configure() { base.Configure(); Post("/api/integrations/qqbot/v1/groups/sync"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var (agentId, request) = await AuthenticateAsync(ct); if (request is null) return;
        await SendAsync(await Service.SyncGroupsAsync(agentId, request, ct), cancellation: ct);
    }
}

public sealed class QqBotAgentLeaseEndpoint(IQqBotAgentService service)
    : QqBotAgentEndpoint<QqBotLeaseRequest, QqBotLeaseResponse>(service)
{
    public override void Configure() { base.Configure(); Post("/api/integrations/qqbot/v1/deliveries/lease"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var (agentId, request) = await AuthenticateAsync(ct); if (request is null) return;
        await SendAsync(await Service.LeaseAsync(agentId, request, ct), cancellation: ct);
    }
}

public sealed class QqBotAgentAckEndpoint(IQqBotAgentService service)
    : QqBotAgentEndpoint<QqBotDeliveryAckRequest, QqBotAgentOperationResult>(service)
{
    public override void Configure() { base.Configure(); Post("/api/integrations/qqbot/v1/deliveries/ack"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var (agentId, request) = await AuthenticateAsync(ct); if (request is null) return;
        await SendAsync(await Service.AcknowledgeAsync(agentId, request, ct), cancellation: ct);
    }
}

public sealed class QqBotAgentFailEndpoint(IQqBotAgentService service)
    : QqBotAgentEndpoint<QqBotDeliveryFailureRequest, QqBotAgentOperationResult>(service)
{
    public override void Configure() { base.Configure(); Post("/api/integrations/qqbot/v1/deliveries/fail"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var (agentId, request) = await AuthenticateAsync(ct); if (request is null) return;
        await SendAsync(await Service.FailAsync(agentId, request, ct), cancellation: ct);
    }
}
