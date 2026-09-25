using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoCTF.Application.Authentication.Sso;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Authentication;

public sealed class RedisSsoFlowStore(IConnectionMultiplexer? redis = null) : ISsoFlowStore
{
    private const string Prefix = "{noctf-sso}";
    private const string CreateScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 or redis.call('EXISTS', KEYS[2]) == 1 then
          return 0
        end
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        redis.call('SET', KEYS[2], KEYS[1], 'PX', ARGV[2])
        return 1
        """;
    private const string ClaimScript = """
        local flowKey = redis.call('GET', KEYS[1])
        if not flowKey then return {1} end
        local value = redis.call('GET', flowKey)
        if not value then redis.call('DEL', KEYS[1]); return {1} end
        local flow = cjson.decode(value)
        if flow.providerId ~= ARGV[1] or flow.browserIdHash ~= ARGV[2] then return {2} end
        if flow.state ~= 0 then return {3} end
        flow.state = 1
        flow.processingToken = ARGV[3]
        local encoded = cjson.encode(flow)
        redis.call('SET', flowKey, encoded, 'PX', ARGV[4])
        redis.call('DEL', KEYS[1])
        return {0, encoded}
        """;
    private const string CompleteScript = """
        local value = redis.call('GET', KEYS[1])
        if not value then return 0 end
        local flow = cjson.decode(value)
        if flow.state ~= 1 or flow.processingToken ~= ARGV[1] then return 0 end
        flow.state = tonumber(ARGV[2])
        flow.processingToken = cjson.null
        if ARGV[3] ~= '' then flow.externalIdentity = cjson.decode(ARGV[3]) end
        if ARGV[4] ~= '' then flow.failureCode = tonumber(ARGV[4]) end
        redis.call('SET', KEYS[1], cjson.encode(flow), 'PX', ARGV[5])
        return 1
        """;
    private const string ReadScript = """
        local value = redis.call('GET', KEYS[1])
        if not value then return {1} end
        local flow = cjson.decode(value)
        if flow.browserIdHash ~= ARGV[1] then return {2} end
        return {0, value}
        """;
    private const string ConsumeScript = """
        local value = redis.call('GET', KEYS[1])
        if not value then return {1} end
        local flow = cjson.decode(value)
        if flow.browserIdHash ~= ARGV[1] then return {2} end
        if flow.state ~= 2 then return {3, value} end
        flow.state = 4
        local encoded = cjson.encode(flow)
        redis.call('DEL', KEYS[1])
        return {0, encoded}
        """;

    public async Task<bool> CreateAsync(SsoFlowRecord flow, CancellationToken ct)
    {
        var db = Database();
        var ttl = flow.ExpiresAt - flow.CreatedAt;
        if (ttl <= TimeSpan.Zero || ttl > SsoRules.FlowLifetime)
            throw new ArgumentOutOfRangeException(nameof(flow), "SSO flow lifetime is invalid.");
        try
        {
            var result = await db.ScriptEvaluateAsync(
                CreateScript,
                [FlowKey(flow.Id), CorrelationKey(flow.CorrelationToken)],
                [Serialize(flow), checked((long)ttl.TotalMilliseconds)]).WaitAsync(ct);
            return (long)result == 1;
        }
        catch (RedisException exception)
        {
            throw new SsoFlowDependencyException(exception);
        }
    }

    public async Task<SsoFlowClaimResult> ClaimCallbackAsync(
        string correlationToken,
        Guid providerId,
        string browserIdHash,
        CancellationToken ct)
    {
        if (redis is null)
            return new(SsoFlowClaimState.DependencyUnavailable);
        try
        {
            var processingToken = RandomToken();
            var result = (RedisResult[])(await Database().ScriptEvaluateAsync(
                ClaimScript,
                [CorrelationKey(correlationToken)],
                [
                    providerId.ToString(),
                    browserIdHash,
                    processingToken,
                    checked((long)SsoRules.ResultLifetime.TotalMilliseconds)
                ]).WaitAsync(ct))!;
            var state = (int)result[0] switch
            {
                0 => SsoFlowClaimState.Claimed,
                2 => SsoFlowClaimState.InvalidCorrelation,
                3 => SsoFlowClaimState.AlreadyProcessed,
                _ => SsoFlowClaimState.NotFound
            };
            return state == SsoFlowClaimState.Claimed
                ? new(state, Deserialize(result[1].ToString()!), processingToken)
                : new(state);
        }
        catch (RedisException)
        {
            return new(SsoFlowClaimState.DependencyUnavailable);
        }
    }

    public Task<bool> SaveAuthenticatedAsync(
        Guid flowId,
        string processingToken,
        SsoExternalIdentity identity,
        CancellationToken ct) => CompleteAsync(
        flowId,
        processingToken,
        SsoFlowState.Authenticated,
        JsonSerializer.Serialize(identity,
            SsoFlowJsonContext.Default.SsoExternalIdentity),
        null,
        ct);

    public Task<bool> SaveFailureAsync(
        Guid flowId,
        string processingToken,
        SsoFailureCode failure,
        CancellationToken ct) => CompleteAsync(
        flowId,
        processingToken,
        SsoFlowState.Failed,
        null,
        failure,
        ct);

    public async Task<SsoFlowReadResult> ReadAsync(
        Guid flowId,
        string browserIdHash,
        CancellationToken ct) =>
        await ReadOrConsumeAsync(ReadScript, flowId, browserIdHash, ct);

    public async Task<SsoFlowReadResult> ConsumeAuthenticatedAsync(
        Guid flowId,
        string browserIdHash,
        CancellationToken ct) =>
        await ReadOrConsumeAsync(ConsumeScript, flowId, browserIdHash, ct);

    private async Task<bool> CompleteAsync(
        Guid flowId,
        string processingToken,
        SsoFlowState state,
        string? identityJson,
        SsoFailureCode? failure,
        CancellationToken ct)
    {
        if (redis is null)
            return false;
        try
        {
            var result = await Database().ScriptEvaluateAsync(
                CompleteScript,
                [FlowKey(flowId)],
                [
                    processingToken,
                    (int)state,
                    identityJson ?? string.Empty,
                    failure is null ? string.Empty : ((int)failure.Value).ToString(),
                    checked((long)SsoRules.ResultLifetime.TotalMilliseconds)
                ]).WaitAsync(ct);
            return (long)result == 1;
        }
        catch (RedisException)
        {
            return false;
        }
    }

    private async Task<SsoFlowReadResult> ReadOrConsumeAsync(
        string script,
        Guid flowId,
        string browserIdHash,
        CancellationToken ct)
    {
        if (redis is null)
            return new(SsoFlowReadState.DependencyUnavailable);
        try
        {
            var result = (RedisResult[])(await Database().ScriptEvaluateAsync(
                script,
                [FlowKey(flowId)],
                [browserIdHash]).WaitAsync(ct))!;
            var state = (int)result[0] switch
            {
                0 => SsoFlowReadState.Available,
                2 => SsoFlowReadState.InvalidCorrelation,
                _ => SsoFlowReadState.NotFound
            };
            return state == SsoFlowReadState.Available
                ? new(state, Deserialize(result[1].ToString()!))
                : new(state);
        }
        catch (RedisException)
        {
            return new(SsoFlowReadState.DependencyUnavailable);
        }
    }

    private IDatabase Database() => redis?.GetDatabase()
        ?? throw new SsoFlowDependencyException();

    private static RedisKey FlowKey(Guid id) => $"{Prefix}:flow:{id:N}";
    private static RedisKey CorrelationKey(string token) =>
        $"{Prefix}:correlation:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))}";
    private static string Serialize(SsoFlowRecord flow) =>
        JsonSerializer.Serialize(flow, SsoFlowJsonContext.Default.SsoFlowRecord);
    private static SsoFlowRecord Deserialize(string value) =>
        JsonSerializer.Deserialize(value, SsoFlowJsonContext.Default.SsoFlowRecord)
        ?? throw new InvalidOperationException("The stored SSO flow is invalid.");
    private static string RandomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SsoFlowRecord))]
[JsonSerializable(typeof(SsoExternalIdentity))]
internal partial class SsoFlowJsonContext : JsonSerializerContext;
