using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Milky;
using NoCTF.Bot.NoCtf;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class ProtocolClientTests
{
    [Test]
    public async Task MilkyClient_UsesSnakeCaseContractAndBearerAuthentication()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();
            return Json("""
                {
                  "status": "ok",
                  "retcode": 0,
                  "data": {
                    "member": {
                      "user_id": 30003,
                      "group_id": 20002,
                      "role": "admin"
                    }
                  },
                  "message": ""
                }
                """);
        });
        var client = new MilkyClient(
            new HttpClient(handler),
            Options.Create(new MilkyOptions
            {
                BaseUrl = new("http://milky.test"),
                AccessToken = "milky-secret"
            }));

        var member = await client.GetGroupMemberAsync(20002, 30003, CancellationToken.None);

        await Assert.That(member.Role).IsEqualTo(MilkyGroupRole.Admin);
        await Assert.That(captured!.RequestUri!.AbsoluteUri)
            .IsEqualTo("http://milky.test/api/get_group_member_info");
        await Assert.That(captured.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(captured.Headers.Authorization.Parameter).IsEqualTo("milky-secret");
        using var document = JsonDocument.Parse(body!);
        await Assert.That(document.RootElement.GetProperty("group_id").GetInt64()).IsEqualTo(20002);
        await Assert.That(document.RootElement.GetProperty("user_id").GetInt64()).IsEqualTo(30003);
    }

    [Test]
    public async Task NoCtfClient_AcceptedResponse_ExposesRetryAfterWithoutBusyLoop()
    {
        var calls = 0;
        var handler = new StubHandler(request =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Accepted);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        });
        var client = new NoCtfClient(
            new HttpClient(handler),
            Options.Create(new NoCtfBotOptions
            {
                BaseUrl = new("https://noctf.test"),
                PublicBaseUrl = new("https://noctf.test"),
                AccessToken = "noctf-secret"
            }),
            TimeProvider.System);
        var competitionId = Guid.NewGuid();

        var first = await client.GetLeaderboardAsync(competitionId, CancellationToken.None);
        var coalesced = await client.GetLeaderboardAsync(competitionId, CancellationToken.None);

        await Assert.That(first.State).IsEqualTo(NoCtfReadState.Processing);
        await Assert.That(first.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(7));
        await Assert.That(coalesced.State).IsEqualTo(NoCtfReadState.Processing);
        await Assert.That(calls).IsEqualTo(1);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
