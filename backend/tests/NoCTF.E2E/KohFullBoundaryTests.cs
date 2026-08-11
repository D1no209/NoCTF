using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NoCTF.E2E;

[Category("KohE2E")]
public sealed class KohFullBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            "koh-e2e-admin",
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
                startTime = now.AddMinutes(-1),
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
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/configuration",
            new
            {
                expectedRevision = 0,
                json = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    pollIntervalSeconds = 2,
                    controlPointsPerInterval = 10
                }, JsonOptions)
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
                definitionJson = BuildDefinition(runtimeImage)
            },
            HttpStatusCode.Created,
            cancellationToken);
        var challenge = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/challenges",
            new { challengeId = template.GetProperty("id").GetGuid(), baseScore = 0, order = 0 },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionChallengeId = challenge.GetProperty("id").GetGuid();
        var challengeRevision = challenge.GetProperty("revision").GetInt32();
        var configuration = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            pollIntervalSeconds = 2,
            controlPointsPerInterval = 10
        }, JsonOptions);
        var configured = await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration",
            new { expectedRevision = challengeRevision, json = configuration },
            HttpStatusCode.OK,
            cancellationToken);
        challengeRevision = configured.GetProperty("revision").GetInt32();
        await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}",
            new { baseScore = 0, order = 0, isPublished = true, expectedRevision = challengeRevision },
            HttpStatusCode.OK,
            cancellationToken);

        await SendWithoutBodyAsync(admin, HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/make-visible",
            HttpStatusCode.NoContent, cancellationToken);
        await SendWithoutBodyAsync(admin, HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/publish",
            HttpStatusCode.NoContent, cancellationToken);
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

        await SendWithoutBodyAsync(admin, HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/start",
            HttpStatusCode.NoContent, cancellationToken);
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
        await Assert.That(runtime.GetProperty("generation").GetInt32()).IsEqualTo(1);
        await Assert.That(runtime.GetProperty("controlCheckUrl").GetString()).Contains("/control");

        var detailPath = $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}";
        var anonymousDetail = await GetJsonAsync(anonymous, detailPath, cancellationToken);
        await Assert.That(anonymousDetail.GetProperty("controlFlag").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        var redDetail = await GetJsonAsync(red.Client, detailPath, cancellationToken);
        var blueDetail = await GetJsonAsync(blue.Client, detailPath, cancellationToken);
        await Assert.That(redDetail.GetProperty("controlFlag").GetString()).IsEqualTo(redFlag);
        await Assert.That(blueDetail.GetProperty("controlFlag").GetString()).IsEqualTo(blueFlag);
        await Assert.That(redDetail.GetProperty("urls").GetArrayLength()).IsEqualTo(1);
        await Assert.That(redDetail.GetProperty("urls")[0].GetString()).Contains("/play");
        await Assert.That(redDetail.ToString()).DoesNotContain("controlCheckUrl");
        var fixtureUrl = ToHostUrl(redDetail.GetProperty("urls")[0].GetString()!);

        await SetFixtureAsync(fixtureUrl, "wrong", null, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(5), cancellationToken);

        var beforeRed = await ReadScoresAsync(anonymous, competitionId, cancellationToken);
        await SetFixtureAsync(fixtureUrl, "flag", redFlag, cancellationToken);
        await PollScoreAsync(
            anonymous, competitionId, red.TeamId,
            beforeRed.GetValueOrDefault(red.TeamId) + 10,
            TimeSpan.FromSeconds(15), cancellationToken);

        await SetFixtureAsync(fixtureUrl, "unavailable", null, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(5), cancellationToken);

        await SetFixtureAsync(fixtureUrl, "timeout", null, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(7), cancellationToken);
        await SetFixtureAsync(fixtureUrl, "wrong", null, cancellationToken);

        const string ambiguousFlag = "flag{koh-ambiguous-control}";
        await UpdateFlagAsync(admin, competitionId, competitionChallengeId,
            byTeam[red.TeamId], ambiguousFlag, cancellationToken);
        await UpdateFlagAsync(admin, competitionId, competitionChallengeId,
            byTeam[blue.TeamId], ambiguousFlag, cancellationToken);
        await SetFixtureAsync(fixtureUrl, "flag", ambiguousFlag, cancellationToken);
        await AssertScoresStableAsync(
            anonymous, competitionId, TimeSpan.FromSeconds(5), cancellationToken);
        await SetFixtureAsync(fixtureUrl, "wrong", null, cancellationToken);
        await UpdateFlagAsync(admin, competitionId, competitionChallengeId,
            byTeam[red.TeamId], redFlag, cancellationToken);
        await UpdateFlagAsync(admin, competitionId, competitionChallengeId,
            byTeam[blue.TeamId], blueFlag, cancellationToken);

        await SendWithoutBodyAsync(admin, HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/pause",
            HttpStatusCode.NoContent, cancellationToken);
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
        await SendWithoutBodyAsync(admin, HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/resume",
            HttpStatusCode.NoContent, cancellationToken);
        await PollScoreAsync(
            anonymous, competitionId, blue.TeamId,
            beforeBlue.GetValueOrDefault(blue.TeamId) + 10,
            TimeSpan.FromSeconds(15), cancellationToken);

        await SendWithoutBodyAsync(admin, HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/finish",
            HttpStatusCode.NoContent, cancellationToken);
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

    private static string BuildDefinition(string runtimeImage) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            runtime = new
            {
                allocation = 0,
                definition = new
                {
                    kind = "container",
                    image = runtimeImage,
                    environment = new Dictionary<string, string>(),
                    labels = new Dictionary<string, string>(),
                    portMappings = new Dictionary<string, int> { ["8080"] = 0 },
                    security = new
                    {
                        noNewPrivileges = true,
                        readonlyRootfs = true,
                        runAsNonRoot = true,
                        capDrop = new[] { "ALL" },
                        capAdd = Array.Empty<string>()
                    },
                    egressPolicy = 0
                },
                limits = new
                {
                    memoryBytes = 67_108_864,
                    nanoCpus = 100_000_000,
                    pidsLimit = 64
                },
                operationTimeoutSeconds = 60,
                urlBindings = new[]
                {
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/play",
                        exposure = 1,
                        containerPort = 8080
                    }
                },
                flagSource = 0,
                controlCheckUrlBinding = new
                {
                    urlTemplate = "http://{HOST}:{PORT}/control",
                    exposure = 0,
                    containerPort = 8080
                }
            }
        }, JsonOptions);

    private static async Task UpdateFlagAsync(
        HttpClient admin,
        Guid competitionId,
        Guid competitionChallengeId,
        JsonElement original,
        string flag,
        CancellationToken cancellationToken) =>
        _ = await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{original.GetProperty("id").GetGuid()}",
            new
            {
                teamId = original.GetProperty("teamId").GetGuid(),
                flag,
                specificationKind = "RuntimeDefinition",
                specificationId = competitionChallengeId
            },
            HttpStatusCode.OK,
            cancellationToken);

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
        leaderboard.GetProperty("entries").EnumerateArray().ToDictionary(
            item => item.GetProperty("teamId").GetGuid(),
            item => item.GetProperty("score").GetInt64());

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
        await SendJsonAsync(anonymous, HttpMethod.Post, "/api/v1/auth/register",
            new { userName, email, password }, HttpStatusCode.Created, cancellationToken);
        var client = CreateClient(baseUrl, await LoginAsync(
            anonymous, userName, password, cancellationToken));
        var team = await SendJsonAsync(
            client, HttpMethod.Post, $"/api/v1/competitions/{competitionId}/teams",
            new { name = teamName }, HttpStatusCode.Created, cancellationToken);
        return new(client, team.GetProperty("id").GetGuid());
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
        var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required for the external KoH E2E test.");

    private sealed record TeamSession(HttpClient Client, Guid TeamId) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
