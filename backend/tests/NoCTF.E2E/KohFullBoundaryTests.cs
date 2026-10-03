using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NoCTF.E2E;

[Category("KohE2E")]
public sealed class KohFullBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LeaderboardProjectionTimeout = TimeSpan.FromSeconds(30);

    [Test]
    [Timeout(420_000)]
    public async Task Koh_flow_crosses_http_worker_runner_polling_and_docker(
        CancellationToken cancellationToken)
    {
        var baseUrl = RequiredEnvironment("NOCTF_E2E_BASE_URL");
        var runtimeImage = RequiredEnvironment("NOCTF_E2E_RUNTIME_IMAGE");
        using var anonymous = CreateClient(baseUrl);
        using var admin = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            E2EHttpClient.AdminUserName("koh-e2e-admin"),
            RequiredEnvironment("NOCTF_E2E_ADMIN_PASSWORD"),
            cancellationToken));

        var now = DateTimeOffset.UtcNow;
        var competition = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/competitions",
            new
            {
                title = "KoH full-boundary E2E",
                description = "Shared Hill, control polling, lifecycle, and scoring verification",
                mode = "Koh",
                startTime = now.AddMinutes(10),
                endTime = now.AddHours(1),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5,
                maxConcurrentRuntimeInstancesPerTeam = 1
            },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionId = competition.GetProperty("id").GetGuid();
        await SendJsonAsync(
            admin,
            HttpMethod.Patch,
            $"/api/v1/admin/competitions/{competitionId}",
            new
            {
                modeConfiguration = new
                {
                    configuration = new
                    {
                        mode = "Koh",
                        flagTemplate = new { header = "flag", bodyTemplate = "[GUID]", leetLiteralText = false },
                        koh = new { pollIntervalSeconds = 2, controlPointsPerInterval = 10L }
                    }
                }
            },
            HttpStatusCode.OK,
            cancellationToken);

        var template = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/challenges",
            new
            {
                visibility = "Private",
                title = "Shared KoH Hill",
                description = "Returns the current controlling Team Flag as a raw response body.",
                direction = "Pwn",
                mode = "Koh",
                definition = BuildDefinition(runtimeImage)
            },
            HttpStatusCode.Created,
            cancellationToken);
        var challenge = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/challenges",
            new { challengeId = template.GetProperty("id").GetGuid(), order = 0 },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionChallengeId = challenge.GetProperty("id").GetGuid();
        var configuration = new
        {
            mode = "Koh",
            koh = new { pollIntervalSeconds = 2, controlPointsPerInterval = 10L }
        };
        await SendJsonAsync(
            admin,
            HttpMethod.Patch,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}",
            new
            {
                presentation = new
                {
                    customTitle = (string?)null,
                    order = 0,
                    isPublished = true
                },
                rules = new { configuration }
            },
            HttpStatusCode.OK,
            cancellationToken);

        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Visible", cancellationToken);
        using var red = await RegisterTeamAsync(
            anonymous, baseUrl, competitionId,
            "koh-red", "red@koh-e2e.test", "koh-red-password", "Red Team", cancellationToken);
        using var blue = await RegisterTeamAsync(
            anonymous, baseUrl, competitionId,
            "koh-blue", "blue@koh-e2e.test", "koh-blue-password", "Blue Team", cancellationToken);

        var generated = await SendWithoutBodyAndReadJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/flags/generate-missing",
            HttpStatusCode.OK,
            cancellationToken);
        await Assert.That(generated.GetProperty("failures").GetArrayLength()).IsEqualTo(0);
        var flags = await GetJsonAsync(
            admin,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags",
            cancellationToken);
        var byTeam = flags.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("teamId").GetGuid(), item => item.Clone());
        await Assert.That(byTeam).Count().IsEqualTo(2);
        await Assert.That(byTeam.Values.All(item =>
            item.GetProperty("specificationKind").GetString() == "RuntimeDefinition"
            && item.GetProperty("specificationId").GetGuid() == competitionChallengeId)).IsTrue();
        var redFlag = byTeam[red.TeamId].GetProperty("flag").GetString()!;
        var blueFlag = byTeam[blue.TeamId].GetProperty("flag").GetString()!;
        await Assert.That(redFlag).IsNotEqualTo(blueFlag);

        await E2ELifecycle.MakeScheduleDueAsync(admin, competitionId, cancellationToken);
        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Published", cancellationToken);
        await E2ELifecycle.StartOrObserveRunningAsync(admin, competitionId, cancellationToken);
        var runtimePath =
            $"/api/v1/admin/competitions/{competitionId}/runtimes?competitionChallengeId={competitionChallengeId}";
        var runtimeList = await PollJsonAsync(
            admin,
            runtimePath,
            value => value.GetProperty("items").GetArrayLength() == 1
                && value.GetProperty("items")[0].GetProperty("state").GetString() == "Running",
            TimeSpan.FromSeconds(90),
            cancellationToken);
        var runtime = runtimeList.GetProperty("items")[0];
        var runtimeId = runtime.GetProperty("id").GetGuid();
        await Assert.That(runtime.GetProperty("teamId").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(runtime.GetProperty("accesses").GetArrayLength()).IsEqualTo(1);
        await Assert.That(E2ELifecycle.FirstDirectAddress(runtime)).Contains("/play");
        await Assert.That(runtime.ToString()).DoesNotContain("controlCheckUrl");

        var detailPath = $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}";
        await SendWithoutBodyAsync(
            anonymous,
            HttpMethod.Get,
            detailPath,
            HttpStatusCode.NotFound,
            cancellationToken);
        var redDetail = await GetJsonAsync(red.Client, detailPath, cancellationToken);
        var blueDetail = await GetJsonAsync(blue.Client, detailPath, cancellationToken);
        await Assert.That(redDetail.GetProperty("controlFlag").GetString()).IsEqualTo(redFlag);
        await Assert.That(blueDetail.GetProperty("controlFlag").GetString()).IsEqualTo(blueFlag);
        await Assert.That(redDetail.GetProperty("accesses").GetArrayLength()).IsEqualTo(1);
        await Assert.That(E2ELifecycle.FirstDirectAddress(redDetail)).Contains("/play");
        await Assert.That(redDetail.ToString()).DoesNotContain("controlCheckUrl");
        var fixtureUrl = ToHostUrl(E2ELifecycle.FirstDirectAddress(redDetail));

        await SetFixtureAsync(fixtureUrl, "wrong", null, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(5), cancellationToken);

        var beforeRed = await ReadScoresAsync(anonymous, competitionId, cancellationToken);
        await SetFixtureAsync(fixtureUrl, "flag", redFlag, cancellationToken);
        await PollScoreAsync(
            anonymous, competitionId, red.TeamId,
            beforeRed.GetValueOrDefault(red.TeamId) + 10,
            LeaderboardProjectionTimeout, cancellationToken);

        await SetFixtureAsync(fixtureUrl, "unavailable", null, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(5), cancellationToken);

        await SetFixtureAsync(fixtureUrl, "timeout", null, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(7), cancellationToken);
        await SetFixtureAsync(fixtureUrl, "wrong", null, cancellationToken);

        const string attemptedReplacement = "flag{koh-manual-replacement}";
        await AssertCompetitionFlagMutationUnavailableAsync(
            admin, competitionId, competitionChallengeId,
            byTeam[red.TeamId], cancellationToken);
        await AssertCompetitionFlagMutationUnavailableAsync(
            admin, competitionId, competitionChallengeId,
            byTeam[blue.TeamId], cancellationToken);
        await SetFixtureAsync(fixtureUrl, "flag", attemptedReplacement, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(5), cancellationToken);
        await SetFixtureAsync(fixtureUrl, "wrong", null, cancellationToken);

        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Paused", cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        await SetFixtureAsync(fixtureUrl, "flag", blueFlag, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(5), cancellationToken);
        var pausedRuntime = await GetJsonAsync(admin, runtimePath, cancellationToken);
        await Assert.That(pausedRuntime.GetProperty("items")[0].GetProperty("id").GetGuid())
            .IsEqualTo(runtimeId);
        await Assert.That(pausedRuntime.GetProperty("items")[0].GetProperty("state").GetString())
            .IsEqualTo("Running");
        var pausedDetail = await GetJsonAsync(blue.Client, detailPath, cancellationToken);
        await Assert.That(pausedDetail.GetProperty("controlFlag").ValueKind)
            .IsEqualTo(JsonValueKind.Null);

        var beforeBlue = await ReadScoresAsync(anonymous, competitionId, cancellationToken);
        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Running", cancellationToken);
        await PollScoreAsync(
            anonymous, competitionId, blue.TeamId,
            beforeBlue.GetValueOrDefault(blue.TeamId) + 10,
            LeaderboardProjectionTimeout, cancellationToken);

        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Finished", cancellationToken);
        var leaderboard = await PollJsonAsync(
            anonymous,
            $"/api/v1/competitions/{competitionId}/leaderboard",
            value => Scores(value).GetValueOrDefault(red.TeamId) >= 10
                && Scores(value).GetValueOrDefault(blue.TeamId) >= 10,
            TimeSpan.FromSeconds(20),
            cancellationToken);
        await Assert.That(Scores(leaderboard)[red.TeamId]).IsGreaterThanOrEqualTo(10);
        await Assert.That(Scores(leaderboard)[blue.TeamId]).IsGreaterThanOrEqualTo(10);

        await PollJsonAsync(
            admin,
            runtimePath,
            value => value.GetProperty("items")[0].GetProperty("state").GetString() == "Stopped",
            TimeSpan.FromSeconds(90),
            cancellationToken);
    }

    private static object BuildDefinition(string runtimeImage) =>
        new
        {
            mode = "Koh",
            koh = new { },
            runtime = new
            {
                kind = "Container",
                allocation = "Shared",
                container = new { services = new[] { new { name = "main", cpuCores = 0.1m, memoryMiB = 64L, image = runtimeImage, command = Array.Empty<string>(), environment = new Dictionary<string, string>(), internalPorts = Array.Empty<int>(), flagEnvironmentVariableName = (string?)null } } },
                egressPolicy = "Isolated",
                operationTimeoutSeconds = 60,
                urlBindings = new[]
                {
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/play",
                        exposure = "Participants",
                        containerPort = 8080,
                        serviceName = "main",
                        vmId = (string?)null,
                        guestPort = (int?)null,
                        isControlCheck = false
                    },
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/control",
                        exposure = "OwnerOnly",
                        containerPort = 8080,
                        serviceName = "main",
                        vmId = (string?)null,
                        guestPort = (int?)null,
                        isControlCheck = true
                    }
                },
                flagSource = "Static"
            }
        };

    private static async Task AssertCompetitionFlagMutationUnavailableAsync(
        HttpClient admin,
        Guid competitionId,
        Guid competitionChallengeId,
        JsonElement original,
        CancellationToken cancellationToken)
    {
        using var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{original.GetProperty("id").GetGuid()}",
            new { flag = "flag{koh-manual-replacement}", matchKind = "Exact" },
            JsonOptions,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.MethodNotAllowed)
            throw new InvalidOperationException(
                $"Expected 405 for unsupported KoH Flag mutation, received {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task SetFixtureAsync(
        Uri fixtureUrl,
        string mode,
        string? value,
        CancellationToken cancellationToken)
    {
        var query = $"mode={Uri.EscapeDataString(mode)}";
        if (value is not null) query += $"&value={Uri.EscapeDataString(value)}";
        var target = new UriBuilder(fixtureUrl) { Path = "/set", Query = query }.Uri;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        HttpStatusCode? lastStatus = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(target, cancellationToken);
                lastStatus = response.StatusCode;
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new InvalidOperationException(
            $"Fixture state change did not become available. Last status: {lastStatus}.");
    }

    private static Uri ToHostUrl(string url)
    {
        var builder = new UriBuilder(url) { Host = "127.0.0.1" };
        return builder.Uri;
    }

    private static Dictionary<Guid, long> Scores(JsonElement leaderboard) =>
        leaderboard.GetProperty("teams").EnumerateArray().ToDictionary(
            item => item.GetProperty("teamId").GetGuid(),
            item => item.GetProperty("totalScore").GetInt64());

    private static async Task<Dictionary<Guid, long>> ReadScoresAsync(
        HttpClient client,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var leaderboard = await PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/leaderboard",
            _ => true,
            TimeSpan.FromSeconds(10),
            cancellationToken);
        return Scores(leaderboard);
    }

    private static async Task AssertScoresStableAsync(
        HttpClient client,
        Guid competitionId,
        TimeSpan observationWindow,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(observationWindow).AddSeconds(15);
        var stableSince = DateTimeOffset.UtcNow;
        var previous = await ReadScoresAsync(client, competitionId, cancellationToken);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var current = await ReadScoresAsync(client, competitionId, cancellationToken);
            if (!current.OrderBy(item => item.Key).SequenceEqual(previous.OrderBy(item => item.Key)))
            {
                previous = current;
                stableSince = DateTimeOffset.UtcNow;
                continue;
            }
            if (DateTimeOffset.UtcNow - stableSince >= observationWindow)
                return;
        }
        throw new TimeoutException(
            $"KoH scores did not remain stable for {observationWindow}. Last scores: "
            + string.Join(", ", previous.Select(item => $"{item.Key:D}={item.Value}")));
    }

    private static Task<JsonElement> PollScoreAsync(
        HttpClient client,
        Guid competitionId,
        Guid teamId,
        long expectedMinimum,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/leaderboard",
            value => Scores(value).GetValueOrDefault(teamId) >= expectedMinimum,
            timeout,
            cancellationToken);

    private static async Task<TeamSession> RegisterTeamAsync(
        HttpClient anonymous,
        string baseUrl,
        Guid competitionId,
        string userName,
        string email,
        string password,
        string teamName,
        CancellationToken cancellationToken)
    {
        userName = E2EHttpClient.UniqueIdentity(userName);
        email = E2EHttpClient.UniqueIdentity(email);
        await SendJsonAsync(anonymous, HttpMethod.Post, "/api/v1/auth/register",
            new { userName, email, password }, HttpStatusCode.Created, cancellationToken);
        var client = CreateClient(baseUrl, await LoginAsync(
            anonymous, userName, password, cancellationToken));
        var team = await SendJsonAsync(
            client, HttpMethod.Post, $"/api/v1/competitions/{competitionId}/teams",
            new { name = teamName, trackKey = "default" }, HttpStatusCode.Created, cancellationToken);
        var teamId = team.GetProperty("id").GetGuid();
        await E2ELifecycle.SubmitTeamRegistrationAsync(
            client, competitionId, teamId, cancellationToken);
        return new(client, teamId);
    }

    private static async Task<JsonElement> PollJsonAsync(
        HttpClient client,
        string path,
        Func<JsonElement, bool> completed,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        JsonElement last = default;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                last = await ReadExpectedJsonAsync(response, HttpStatusCode.OK, cancellationToken);
                if (completed(last)) return last;
            }
            else if (response.StatusCode != HttpStatusCode.Accepted)
            {
                throw await UnexpectedResponseAsync(response, HttpStatusCode.OK, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Polling {path} timed out. Last response: {last}");
    }

    private static async Task<JsonElement> GetJsonAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        return await ReadExpectedJsonAsync(response, HttpStatusCode.OK, cancellationToken);
    }

    private static async Task<JsonElement> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        E2ELifecycle.AddIdempotencyKey(request);
        using var response = await client.SendAsync(request, cancellationToken);
        return await ReadExpectedJsonAsync(response, expected, cancellationToken);
    }

    private static async Task SendWithoutBodyAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        E2ELifecycle.AddIdempotencyKey(request);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != expected)
            throw await UnexpectedResponseAsync(response, expected, cancellationToken);
    }

    private static async Task<JsonElement> SendWithoutBodyAndReadJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        E2ELifecycle.AddIdempotencyKey(request);
        using var response = await client.SendAsync(request, cancellationToken);
        return await ReadExpectedJsonAsync(response, expected, cancellationToken);
    }

    private static async Task<JsonElement> ReadExpectedJsonAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode != expected)
            throw await UnexpectedResponseAsync(response, expected, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private static async Task<InvalidOperationException> UnexpectedResponseAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new InvalidOperationException(
            $"Expected {(int)expected} but received {(int)response.StatusCode} for {response.RequestMessage?.RequestUri}: {body}");
    }

    private static async Task<string> LoginAsync(
        HttpClient client,
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync(
            client, HttpMethod.Post, "/api/v1/auth/login",
            new { login, password }, HttpStatusCode.OK, cancellationToken);
        return response.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login response did not contain an access token.");
    }

    private static HttpClient CreateClient(string baseUrl, string? token = null)
    {
        return E2EHttpClient.Create(baseUrl, token);
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required for the external KoH E2E test.");

    private sealed record TeamSession(HttpClient Client, Guid TeamId) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
