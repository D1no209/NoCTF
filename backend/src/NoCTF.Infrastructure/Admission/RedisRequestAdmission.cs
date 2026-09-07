using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Admission;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Admission;

/// <summary>One atomic acquisition for independent account/IP quotas and renewable concurrency leases.</summary>
public sealed class RedisRequestAdmission(IConnectionMultiplexer? redis = null) : IRequestAdmission
{
    private const string AcquireScript = """
        local time = redis.call('TIME')
        local now = time[1] * 1000 + math.floor(time[2] / 1000)
        local nr = tonumber(ARGV[3])
        local nc = tonumber(ARGV[4])
        for i=1,nr do
          if tonumber(redis.call('GET',KEYS[i]) or '0') >= tonumber(ARGV[4+i*2-1]) then
            return {1, math.max(1,math.ceil(redis.call('PTTL',KEYS[i])/1000))}
          end
        end
        for i=1,nc do
          local key = KEYS[nr+i]
          redis.call('ZREMRANGEBYSCORE',key,'-inf',now)
          if redis.call('ZCARD',key) >= tonumber(ARGV[4+nr*2+i]) then return {2,1} end
        end
        for i=1,nr do
          if redis.call('INCR',KEYS[i]) == 1 then redis.call('EXPIRE',KEYS[i],ARGV[4+i*2]) end
        end
        for i=1,nc do
          local key = KEYS[nr+i]
          redis.call('ZADD',key,now+tonumber(ARGV[2]),ARGV[1])
          redis.call('PEXPIRE',key,tonumber(ARGV[2])*2)
        end
        return {0,0}
        """;
    private const string RenewScript = """
        local time = redis.call('TIME')
        local now = time[1]*1000+math.floor(time[2]/1000)
        for _,key in ipairs(KEYS) do
          local expiry=redis.call('ZSCORE',key,ARGV[1])
          if not expiry or tonumber(expiry)<=now then return 0 end
        end
        for _,key in ipairs(KEYS) do
          redis.call('ZADD',key,now+tonumber(ARGV[2]),ARGV[1])
          redis.call('PEXPIRE',key,tonumber(ARGV[2])*2)
        end
        return 1
        """;
    private const string ReleaseScript = "for _,key in ipairs(KEYS) do redis.call('ZREM',key,ARGV[1]) end return 1";
    private const int LeaseMilliseconds = 30_000;

    public async ValueTask<IRequestAdmissionLease> AcquireAsync(IReadOnlyList<RateQuota> rates,
        IReadOnlyList<ConcurrentQuota> concurrency, CancellationToken ct)
    {
        if (redis is null) throw new AdmissionRejectedException(AdmissionFailure.DependencyUnavailable);
        var id = Guid.NewGuid().ToString("N");
        var keys = rates.Select(x => Key("rate", x.Key)).Concat(concurrency.Select(x => Key("lease", x.Key))).ToArray();
        var values = new List<RedisValue> { id, LeaseMilliseconds, rates.Count, concurrency.Count };
        foreach (var rate in rates) { values.Add(rate.Limit); values.Add(rate.WindowSeconds); }
        values.AddRange(concurrency.Select(x => (RedisValue)x.Limit));
        try
        {
            var db = redis.GetDatabase();
            var result = (RedisResult[])(await db.ScriptEvaluateAsync(AcquireScript, keys, values.ToArray()).WaitAsync(ct))!;
            var outcome = (int)result[0];
            if (outcome != 0) throw new AdmissionRejectedException(outcome == 1 ? AdmissionFailure.RateLimited : AdmissionFailure.CapacityBusy, (int)result[1]);
            return new Lease(db, keys.Skip(rates.Count).ToArray(), id, ct);
        }
        catch (RedisException) { throw new AdmissionRejectedException(AdmissionFailure.DependencyUnavailable); }
    }

    private static RedisKey Key(string prefix, string identity) =>
        $"{{noctf-request-admission}}:{prefix}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";

    private sealed class Lease : IRequestAdmissionLease
    {
        private readonly IDatabase db;
        private readonly RedisKey[] keys;
        private readonly string id;
        private readonly CancellationTokenSource source;
        private readonly CancellationTokenSource stopping = new();
        private readonly Task renewal;
        public CancellationToken Token => source.Token;
        public Lease(IDatabase db, RedisKey[] keys, string id, CancellationToken ct)
        {
            this.db = db; this.keys = keys; this.id = id;
            source = CancellationTokenSource.CreateLinkedTokenSource(ct);
            renewal = keys.Length == 0 ? Task.CompletedTask : RenewAsync();
        }
        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                while (await timer.WaitForNextTickAsync(stopping.Token))
                    if ((int)await db.ScriptEvaluateAsync(RenewScript, keys, [id, LeaseMilliseconds]).WaitAsync(stopping.Token) != 1)
                    { await source.CancelAsync(); return; }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (Exception) { await source.CancelAsync(); }
        }
        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync(); await renewal;
            if (keys.Length > 0)
                try { await db.ScriptEvaluateAsync(ReleaseScript, keys, [id]).WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception) { /* Expiring lease is the release backstop, never change a committed response. */ }
            source.Dispose(); stopping.Dispose();
        }
    }
}
