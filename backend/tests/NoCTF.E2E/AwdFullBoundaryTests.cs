using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NoCTF.E2E;

[Category("AwdE2E")]
public sealed class AwdFullBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(360_000)]
    public async Task Awd_flow_crosses_http_worker_runner_checkers_and_docker(
        CancellationToken cancellationToken)
    {
        var baseUrl = RequiredEnvironment("NOCTF_E2E_BASE_URL");
        var runtimeImage = RequiredEnvironment("NOCTF_E2E_RUNTIME_IMAGE");
        var checkerImage = RequiredEnvironment("NOCTF_E2E_CHECKER_IMAGE");
        using var anonymous = CreateClient(baseUrl);
        using var admin = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            "awd-e2e-admin",
            RequiredEnvironment("NOCTF_E2E_ADMIN_PASSWORD"),
            cancellationToken));

        var now = DateTimeOffset.UtcNow;
        var competition = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/competitions",
            new
            {
                title = "AWD full-boundary E2E",
                description = "Round, attack, checker, and runtime boundary verification",
                mode = 1,
                startTime = now.AddMinutes(-1),
                endTime = now.AddHours(1),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5
            },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionId = competition.GetProperty("id").GetGuid();

        var competitionConfigurationJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            hardeningDurationSeconds = 20,
            roundDurationSeconds = 30,
            attackRewardMode = "FixedPerAttack",
            attackPoints = 13,
            victimDefensePoolPoints = 17,
            checkerIntervalSeconds = 2,
            serviceHealthyPoints = 11,
            serviceUnhealthyPenalty = 7
        }, JsonOptions);
        await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/configuration",
            new { expectedRevision = 0, json = competitionConfigurationJson },
            HttpStatusCode.OK,
            cancellationToken);

        var template = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/challenges",
            new
            {
                visibility = 0,
                title = "Rotating AWD Service",
                description = "Exposes rotating Flags and a controllable health endpoint.",
                direction = "Pwn"
            },
            HttpStatusCode.Created,
            cancellationToken);
        var templateId = template.GetProperty("id").GetGuid();

        var challenge = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/challenges",
            new { challengeId = templateId, baseScore = 100, order = 0 },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionChallengeId = challenge.GetProperty("id").GetGuid();
        var challengeRevision = challenge.GetProperty("revision").GetInt32();

        var challengeConfigurationJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            runtime = new
            {
                provider = 0,
                allocation = 1,
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
                runnerPool = "awd-e2e",
                urlBindings = new[]
                {
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/",
                        exposure = 1,
                        containerPort = 8080
                    }
                },
                flagSource = 2
            },
            checker = new
            {
                job = new
                {
                    provider = 0,
                    image = checkerImage,
                    command = Array.Empty<string>(),
                    environment = new Dictionary<string, string>(),
                    timeoutSeconds = 15
                },
                target = new
                {
                    kind = "container",
                    urlTemplate = "http://{HOST}:{PORT}/cgi-bin/health",
                    containerPort = 8080
                }
            },
            flagInjection = new
            {
                command = "printf '%s' '${FLAG}' > /dev/shm/flag",
                timeoutSeconds = 5
            }
        }, JsonOptions);
        var updatedConfiguration = await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration",
            new { expectedRevision = challengeRevision, json = challengeConfigurationJson },
            HttpStatusCode.OK,
            cancellationToken);
        challengeRevision = updatedConfiguration.GetProperty("revision").GetInt32();
        await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}",
            new { baseScore = 100, order = 0, isPublished = true, expectedRevision = challengeRevision },
            HttpStatusCode.OK,
            cancellationToken);

        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/make-visible",
            HttpStatusCode.NoContent,
            cancellationToken);
        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/publish",
            HttpStatusCode.NoContent,
            cancellationToken);

        var red = await RegisterTeamAsync(
            anonymous,
            baseUrl,
            competitionId,
            "awd-red",
            "red@awd-e2e.test",
            "awd-red-password",
            "Red Team",
            cancellationToken);
        var blue = await RegisterTeamAsync(
            anonymous,
            baseUrl,
            competitionId,
            "awd-blue",
            "blue@awd-e2e.test",
            "awd-blue-password",
            "Blue Team",
            cancellationToken);

        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/start",
            HttpStatusCode.NoContent,
            cancellationToken);

        var runtimePath = $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime";
        var redRuntime = await PollOptionalJsonAsync(
            red.Client,
            runtimePath,
            value => value.GetProperty("state").GetInt32() == 2,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        var blueRuntime = await PollOptionalJsonAsync(
            blue.Client,
            runtimePath,
            value => value.GetProperty("state").GetInt32() == 2,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        var redRuntimeId = redRuntime.GetProperty("id").GetGuid();
        var blueRuntimeId = blueRuntime.GetProperty("id").GetGuid();
        await Assert.That(redRuntimeId).IsNotEqualTo(blueRuntimeId);
        var redRuntimeUrl = RequiredFirstUrl(redRuntime);
        var blueRuntimeUrl = RequiredFirstUrl(blueRuntime);

        var targetPath = $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/targets";
        var redHardeningTargets = await GetJsonAsync(red.Client, targetPath, cancellationToken);
        var blueHardeningTargets = await GetJsonAsync(blue.Client, targetPath, cancellationToken);
        await AssertSingleTargetAsync(redHardeningTargets, red.TeamId);
        await AssertSingleTargetAsync(blueHardeningTargets, blue.TeamId);

        var hardeningSubmissionId = await SubmitSingleFlagAsync(
            red.Client,
            competitionId,
            competitionChallengeId,
            "flag{hardening-probe}",
            cancellationToken);
        var hardeningSubmission = await PollSubmissionAsync(
            red.Client,
            competitionId,
            hardeningSubmissionId,
            cancellationToken);
        await AssertSubmissionAsync(hardeningSubmission, result: 5, failureCode: 23);

        await PollJsonAsync(
            red.Client,
            targetPath,
            value => value.GetProperty("items").GetArrayLength() == 2,
            TimeSpan.FromSeconds(40),
            cancellationToken);
        await PollJsonAsync(
            blue.Client,
            targetPath,
            value => value.GetProperty("items").GetArrayLength() == 2,
            TimeSpan.FromSeconds(10),
            cancellationToken);

        var redRoundOneFlag = await PollFixtureTextAsync(
            redRuntimeUrl,
            "cgi-bin/flag",
            value => value.StartsWith("flag{", StringComparison.Ordinal),
            TimeSpan.FromSeconds(40),
            cancellationToken);
        var blueRoundOneFlag = await PollFixtureTextAsync(
            blueRuntimeUrl,
            "cgi-bin/flag",
            value => value.StartsWith("flag{", StringComparison.Ordinal),
            TimeSpan.FromSeconds(40),
            cancellationToken);
        await Assert.That(redRoundOneFlag).IsNotEqualTo(blueRoundOneFlag);

        await GetFixtureTextAsync(redRuntimeUrl, "cgi-bin/down", cancellationToken);

        var batch = await SendJsonAsync(
            red.Client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
            new { flags = new[] { blueRoundOneFlag, "flag{wrong-batch}" } },
            HttpStatusCode.Accepted,
            cancellationToken);
        var batchIds = batch.GetProperty("submissions").EnumerateArray()
            .Select(item => item.GetProperty("submissionId").GetGuid())
            .ToArray();
        await Assert.That(batchIds.Length).IsEqualTo(2);
        var correctBatchSubmission = await PollSubmissionAsync(
            red.Client,
            competitionId,
            batchIds[0],
            cancellationToken);
        var wrongBatchSubmission = await PollSubmissionAsync(
            red.Client,
            competitionId,
            batchIds[1],
            cancellationToken);
        await AssertSubmissionAsync(correctBatchSubmission, result: 0, failureCode: null);
        await AssertSubmissionAsync(wrongBatchSubmission, result: 1, failureCode: null);

        var duplicateId = await SubmitSingleFlagAsync(
            red.Client,
            competitionId,
            competitionChallengeId,
            blueRoundOneFlag,
            cancellationToken);
        var duplicate = await PollSubmissionAsync(
            red.Client,
            competitionId,
            duplicateId,
            cancellationToken);
        await AssertSubmissionAsync(duplicate, result: 2, failureCode: 14);

        var blueRoundTwoFlag = await PollFixtureTextAsync(
            blueRuntimeUrl,
            "cgi-bin/flag",
            value => value.StartsWith("flag{", StringComparison.Ordinal)
                && !string.Equals(value, blueRoundOneFlag, StringComparison.Ordinal),
            TimeSpan.FromSeconds(45),
            cancellationToken);

        await GetFixtureTextAsync(redRuntimeUrl, "cgi-bin/up", cancellationToken);
        await PollLeaderboardAsync(
            anonymous,
            competitionId,
            new Dictionary<Guid, long>
            {
                [red.TeamId] = 6,
                [blue.TeamId] = -6
            },
            TimeSpan.FromSeconds(45),
            cancellationToken);

        var expiredId = await SubmitSingleFlagAsync(
            red.Client,
            competitionId,
            competitionChallengeId,
            blueRoundOneFlag,
            cancellationToken);
        var expired = await PollSubmissionAsync(
            red.Client,
            competitionId,
            expiredId,
            cancellationToken);
        await AssertSubmissionAsync(expired, result: 1, failureCode: 21);

        _ = await PollFixtureTextAsync(
            blueRuntimeUrl,
            "cgi-bin/flag",
            value => value.StartsWith("flag{", StringComparison.Ordinal)
                && !string.Equals(value, blueRoundTwoFlag, StringComparison.Ordinal),
            TimeSpan.FromSeconds(45),
            cancellationToken);
        var finalLeaderboard = await PollLeaderboardAsync(
            anonymous,
            competitionId,
            new Dictionary<Guid, long>
            {
                [red.TeamId] = 17,
                [blue.TeamId] = 5
            },
            TimeSpan.FromSeconds(45),
            cancellationToken);
        var redEntry = finalLeaderboard.GetProperty("entries").EnumerateArray()
            .Single(item => item.GetProperty("teamId").GetGuid() == red.TeamId);
        await Assert.That(redEntry.GetProperty("rank").GetInt32()).IsEqualTo(1);
        await Assert.That(redEntry.GetProperty("solveCount").GetInt32()).IsEqualTo(1);

        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/finish",
            HttpStatusCode.NoContent,
            cancellationToken);
        await PollJsonAsync(
            admin,
            $"/api/v1/admin/competitions/{competitionId}/runtimes?competitionChallengeId={competitionChallengeId}",
            value => value.GetProperty("items").GetArrayLength() == 2
                && value.GetProperty("items").EnumerateArray()
                    .All(item => item.GetProperty("state").GetInt32() == 4),
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await PollDockerCleanupAsync(
            new[] { redRuntimeId, blueRuntimeId },
            TimeSpan.FromSeconds(60),
            cancellationToken);
    }

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
        await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/v1/auth/register",
            new { userName, email, password },
            HttpStatusCode.Created,
            cancellationToken);
        var client = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            userName,
            password,
            cancellationToken));
        var team = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/teams",
            new { name = teamName },
            HttpStatusCode.Created,
            cancellationToken);
        return new(client, team.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> SubmitSingleFlagAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        string flag,
        CancellationToken cancellationToken)
    {
        var accepted = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
            new { flag },
            HttpStatusCode.Accepted,
            cancellationToken);
        return accepted.GetProperty("submissionId").GetGuid();
    }

    private static Task<JsonElement> PollSubmissionAsync(
        HttpClient client,
        Guid competitionId,
        Guid submissionId,
        CancellationToken cancellationToken) =>
        PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/submissions/{submissionId}",
            value => value.GetProperty("evaluationState").GetInt32() == 3,
            TimeSpan.FromSeconds(30),
            cancellationToken);

    private static async Task AssertSubmissionAsync(
        JsonElement submission,
        int result,
        int? failureCode)
    {
        await Assert.That(submission.GetProperty("result").GetInt32()).IsEqualTo(result);
        var failure = submission.GetProperty("failureCode");
        if (failureCode is null)
            await Assert.That(failure.ValueKind).IsEqualTo(JsonValueKind.Null);
        else
            await Assert.That(failure.GetInt32()).IsEqualTo(failureCode.Value);
    }

    private static async Task AssertSingleTargetAsync(JsonElement response, Guid teamId)
    {
        var items = response.GetProperty("items");
        await Assert.That(items.GetArrayLength()).IsEqualTo(1);
        await Assert.That(items[0].GetProperty("teamId").GetGuid()).IsEqualTo(teamId);
        await Assert.That(items[0].GetProperty("urls").GetArrayLength()).IsEqualTo(1);
    }

    private static string RequiredFirstUrl(JsonElement runtime) =>
        runtime.GetProperty("urls")[0].GetString()
        ?? throw new InvalidOperationException("The running AWD Runtime did not expose a URL.");

    private static async Task<string> GetFixtureTextAsync(
        string runtimeUrl,
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync(new Uri(new Uri(runtimeUrl), relativePath), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Fixture endpoint {response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}.");
        return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
    }

    private static async Task<string> PollFixtureTextAsync(
        string runtimeUrl,
        string relativePath,
        Func<string, bool> completed,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        string? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                last = await GetFixtureTextAsync(runtimeUrl, relativePath, cancellationToken);
                if (completed(last)) return last;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException(
            $"Fixture endpoint {relativePath} did not reach the expected value. Last value: {last}");
    }

    private static async Task<JsonElement> PollLeaderboardAsync(
        HttpClient client,
        Guid competitionId,
        IReadOnlyDictionary<Guid, long> expectedScores,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/competitions/{competitionId}/leaderboard";
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        JsonElement last = default;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                last = await ReadExpectedJsonAsync(response, HttpStatusCode.OK, cancellationToken);
                var scores = last.GetProperty("entries").EnumerateArray()
                    .ToDictionary(
                        item => item.GetProperty("teamId").GetGuid(),
                        item => item.GetProperty("score").GetInt64());
                if (!last.GetProperty("stale").GetBoolean()
                    && expectedScores.All(expected =>
                        scores.GetValueOrDefault(expected.Key, long.MinValue) == expected.Value))
                    return last;
            }
            else if (response.StatusCode != HttpStatusCode.Accepted)
            {
                throw await UnexpectedResponseAsync(response, HttpStatusCode.OK, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Leaderboard did not reach the expected AWD scores. Last response: {last}");
    }

    private static async Task PollDockerCleanupAsync(
        IReadOnlyList<Guid> runtimeIds,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var hasResources = false;
            foreach (var runtimeId in runtimeIds)
            {
                var label = $"label=noctf.io/runtime-instance-id={runtimeId:D}";
                var containers = await RunProcessAsync(
                    "docker", ["ps", "-aq", "--filter", label], cancellationToken);
                var networks = await RunProcessAsync(
                    "docker", ["network", "ls", "-q", "--filter", label], cancellationToken);
                if (!string.IsNullOrWhiteSpace(containers)
                    || !string.IsNullOrWhiteSpace(networks))
                {
                    hasResources = true;
                    break;
                }
            }
            if (!hasResources) return;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException("AWD Runtime, Checker, or network resources were not cleaned up.");
    }

    private static async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} exited with {process.ExitCode}: {error}");
        return output.Trim();
    }

    private static HttpClient CreateClient(string baseUrl, string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> LoginAsync(
        HttpClient client,
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { login, password },
            HttpStatusCode.OK,
            cancellationToken);
        return response.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login response did not contain an access token.");
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

    private static async Task<JsonElement> GetJsonAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        return await ReadExpectedJsonAsync(response, HttpStatusCode.OK, cancellationToken);
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

    private static async Task<JsonElement> PollOptionalJsonAsync(
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
            else if (response.StatusCode != HttpStatusCode.NotFound)
            {
                throw await UnexpectedResponseAsync(response, HttpStatusCode.OK, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Polling {path} timed out. Last response: {last}");
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
            last = await GetJsonAsync(client, path, cancellationToken);
            if (completed(last)) return last;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Polling {path} timed out. Last response: {last}");
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required for the external AWD E2E test.");

    private sealed record TeamSession(HttpClient Client, Guid TeamId) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
