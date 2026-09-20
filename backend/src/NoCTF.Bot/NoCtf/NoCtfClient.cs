using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;

namespace NoCTF.Bot.NoCtf;

public sealed class NoCtfClient(
    HttpClient http,
    IOptions<NoCtfBotOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LeaderboardMinimumInterval = TimeSpan.FromSeconds(2);
    private readonly NoCtfBotOptions options = options.Value;
    private readonly ConcurrentDictionary<Guid, LeaderboardReadGate> leaderboardGates = new();

    public Task<NoCtfReadResult<CurrentUser>> GetCurrentUserAsync(CancellationToken ct) =>
        GetAsync<CurrentUser>("api/v1/auth/me", ct);

    public Task<NoCtfReadResult<Competition>> GetCompetitionAsync(
        Guid competitionId,
        CancellationToken ct) =>
        GetAsync<Competition>($"api/v1/competitions/{competitionId:D}", ct);

    public Task<NoCtfReadResult<ChallengeList>> GetChallengesAsync(
        Guid competitionId,
        CancellationToken ct) =>
        GetAsync<ChallengeList>($"api/v1/competitions/{competitionId:D}/challenges", ct);

    public async Task<NoCtfReadResult<ScoreboardSnapshot>> GetLeaderboardAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var gate = leaderboardGates.GetOrAdd(competitionId, static _ => new());
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (gate.LastResult is not null
                && now - gate.LastReadAt < LeaderboardMinimumInterval)
            {
                return gate.LastResult;
            }
            var result = await GetAsync<ScoreboardSnapshot>(
                $"api/v1/competitions/{competitionId:D}/leaderboard",
                ct);
            gate.LastReadAt = now;
            gate.LastResult = result;
            return result;
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    private async Task<NoCtfReadResult<T>> GetAsync<T>(
        string path,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            return value is null
                ? new(NoCtfReadState.Unavailable)
                : NoCtfReadResult<T>.Available(value);
        }
        return response.StatusCode switch
        {
            HttpStatusCode.Accepted => new(
                NoCtfReadState.Processing,
                RetryAfter: ReadRetryAfter(response.Headers.RetryAfter)),
            HttpStatusCode.NotFound => new(NoCtfReadState.NotFound),
            HttpStatusCode.Unauthorized => new(NoCtfReadState.Unauthorized),
            HttpStatusCode.ServiceUnavailable => new(
                NoCtfReadState.Unavailable,
                RetryAfter: ReadRetryAfter(response.Headers.RetryAfter)),
            _ => new(NoCtfReadState.Unavailable)
        };
    }

    private TimeSpan ReadRetryAfter(RetryConditionHeaderValue? value)
    {
        if (value?.Delta is { } delta)
            return ClampRetryAfter(delta);
        if (value?.Date is { } date)
            return ClampRetryAfter(date - timeProvider.GetUtcNow());
        return TimeSpan.FromSeconds(2);
    }

    private static TimeSpan ClampRetryAfter(TimeSpan value) =>
        value < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : value > TimeSpan.FromMinutes(5)
                ? TimeSpan.FromMinutes(5)
                : value;

    private sealed class LeaderboardReadGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public DateTimeOffset LastReadAt { get; set; } = DateTimeOffset.MinValue;
        public NoCtfReadResult<ScoreboardSnapshot>? LastResult { get; set; }
    }
}
