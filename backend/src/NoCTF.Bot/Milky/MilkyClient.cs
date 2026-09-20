using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;

namespace NoCTF.Bot.Milky;

public sealed class MilkyClient(HttpClient http, IOptions<MilkyOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
    private readonly MilkyOptions options = options.Value;

    public Task<MilkyLoginInfo> GetLoginInfoAsync(CancellationToken ct) =>
        PostAsync<object, MilkyLoginInfo>("get_login_info", new { }, ct);

    public Task<MilkyImplementationInfo> GetImplementationInfoAsync(CancellationToken ct) =>
        PostAsync<object, MilkyImplementationInfo>("get_impl_info", new { }, ct);

    public async Task<MilkyGroupMember> GetGroupMemberAsync(
        long groupId,
        long userId,
        CancellationToken ct)
    {
        var response = await PostAsync<object, MilkyGroupMemberResponse>(
            "get_group_member_info",
            new { group_id = groupId, user_id = userId, no_cache = false },
            ct);
        return response.Member;
    }

    public Task<MilkySendResult> SendGroupMessageAsync(
        long groupId,
        string text,
        CancellationToken ct) =>
        PostAsync<object, MilkySendResult>(
            "send_group_message",
            new
            {
                group_id = groupId,
                message = new[] { new { type = "text", data = new { text } } }
            },
            ct);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string action,
        TRequest payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(options.BaseUrl, $"api/{action}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<MilkyApiEnvelope<TResponse>>(
            JsonOptions,
            ct) ?? throw new MilkyApiException(-1, "empty_response");
        if (!string.Equals(envelope.Status, "ok", StringComparison.OrdinalIgnoreCase)
            || envelope.Retcode != 0
            || envelope.Data is null)
        {
            throw new MilkyApiException(
                envelope.Retcode,
                string.IsNullOrWhiteSpace(envelope.Message) ? "request_rejected" : envelope.Message);
        }
        return envelope.Data;
    }
}
