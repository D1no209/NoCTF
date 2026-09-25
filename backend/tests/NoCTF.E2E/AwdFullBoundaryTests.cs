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
                mode = "Awd",
                startTime = now.AddMinutes(10),
                endTime = now.AddHours(1),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5,
                maxConcurrentRuntimeInstancesPerTeam = 1
            },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionId = competition.GetProperty("id").GetGuid();

        var competitionConfiguration = new
        {
            mode = "Awd",
            flagTemplate = new { header = "flag", bodyTemplate = "[GUID]", leetLiteralText = false },
            hardeningDurationSeconds = 20,
            roundDurationSeconds = 30,
            attackRewardMode = "FixedPerAttack",
            attackPoints = 13L,
            victimDefensePoolPoints = 17L,
            checkerIntervalSeconds = 2,
            serviceHealthyPoints = 11L,
            serviceUnhealthyPenalty = 7L
        };
        await SendJsonAsync(
            admin,
            HttpMethod.Patch,
            $"/api/v1/admin/competitions/{competitionId}",
            new { modeConfiguration = new { configuration = competitionConfiguration } },
            HttpStatusCode.OK,
            cancellationToken);

        var template = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/challenges",
            new
            {
                mode = "Awd",
                visibility = "Private",
                title = "Rotating AWD Service",
                description = "Exposes rotating Flags and a controllable health endpoint.",
                direction = "Pwn",
                definition = BuildDefinition(runtimeImage, checkerImage)
            },
            HttpStatusCode.Created,
            cancellationToken);
        var templateId = template.GetProperty("id").GetGuid();

        var challenge = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/challenges",
            new { challengeId = templateId, order = 0 },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionChallengeId = challenge.GetProperty("id").GetGuid();

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
                rules = new { configuration = new { mode = "Awd" } }
            },
            HttpStatusCode.OK,
            cancellationToken);

        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Visible", cancellationToken);
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

        await E2ELifecycle.MakeScheduleDueAsync(admin, competitionId, cancellationToken);
        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Published", cancellationToken);
        await E2ELifecycle.StartOrObserveRunningAsync(admin, competitionId, cancellationToken);

        var runtimePath = $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/current";
        var redRuntime = await PollOptionalJsonAsync(
            red.Client,
            runtimePath,
            value => value.GetProperty("state").GetString() == "Running",
            TimeSpan.FromSeconds(90),
            cancellationToken);
        var blueRuntime = await PollOptionalJsonAsync(
            blue.Client,
            runtimePath,
            value => value.GetProperty("state").GetString() == "Running",
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

        var hardeningGameplayFactId = await SubmitSingleFlagAsync(
            red.Client,
            competitionId,
            competitionChallengeId,
            "flag{hardening-probe}",
            cancellationToken);
        var hardeningSubmission = await PollSubmissionAsync(
            red.Client,
            competitionId,
            hardeningGameplayFactId,
            cancellationToken);
        await AssertSubmissionAsync(
            hardeningSubmission,
            result: "Rejected",
            failureCode: "HardeningActive");

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
        await GetFixtureTextAsync(blueRuntimeUrl, "cgi-bin/down", cancellationToken);

        var batch = await SendJsonAsync(
            red.Client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
            new { flags = new[] { blueRoundOneFlag, "flag{wrong-batch}" } },
            HttpStatusCode.Accepted,
            cancellationToken);
        var batchIds = batch.GetProperty("submissions").EnumerateArray()
            .Select(item => item.GetProperty("gameplayFactId").GetGuid())
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
        await AssertSubmissionAsync(correctBatchSubmission, result: "Correct", failureCode: null);
        await AssertSubmissionAsync(wrongBatchSubmission, result: "Wrong", failureCode: null);

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
        await AssertSubmissionAsync(
            duplicate,
            result: "Duplicate",
            failureCode: "DuplicateAttack");

        var blueRoundTwoFlag = await PollFixtureTextAsync(
            blueRuntimeUrl,
            "cgi-bin/flag",
            value => value.StartsWith("flag{", StringComparison.Ordinal)
                && !string.Equals(value, blueRoundOneFlag, StringComparison.Ordinal),
            TimeSpan.FromSeconds(75),
            cancellationToken);

        var firstRoundLeaderboard = await PollLeaderboardWhereAsync(
            anonymous,
            competitionId,
            scores => scores.GetValueOrDefault(red.TeamId, long.MinValue) is 6 or 24
                && scores.GetValueOrDefault(blue.TeamId, long.MinValue) is -24 or -6,
            "the first completed AWD round",
            TimeSpan.FromSeconds(45),
            cancellationToken);
        var firstRoundScores = firstRoundLeaderboard.GetProperty("teams").EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("teamId").GetGuid(),
                item => item.GetProperty("totalScore").GetInt64());
        var scoringBaseline = firstRoundScores;
        WriteRoundScores("first completed round", scoringBaseline, red.TeamId, blue.TeamId);
        var currentBlueFlag = blueRoundTwoFlag;
        var downObserved = firstRoundScores[red.TeamId] == 6
            && firstRoundScores[blue.TeamId] == -24;
        for (var attempt = 0; attempt < 4 && !downObserved; attempt++)
        {
            currentBlueFlag = await PollFixtureTextAsync(
                blueRuntimeUrl,
                "cgi-bin/flag",
                value => value.StartsWith("flag{", StringComparison.Ordinal)
                    && !string.Equals(value, currentBlueFlag, StringComparison.Ordinal),
                TimeSpan.FromSeconds(75),
                cancellationToken);
            var previous = scoringBaseline;
            scoringBaseline = (await PollLeaderboardWhereAsync(
                    anonymous,
                    competitionId,
                    scores => scores.TryGetValue(red.TeamId, out var redScore)
                        && redScore != previous[red.TeamId]
                        && scores.TryGetValue(blue.TeamId, out var blueScore)
                        && blueScore != previous[blue.TeamId],
                    "the next completed AWD round",
                    TimeSpan.FromSeconds(45),
                    cancellationToken))
                .GetProperty("teams").EnumerateArray()
                .ToDictionary(
                    item => item.GetProperty("teamId").GetGuid(),
                    item => item.GetProperty("totalScore").GetInt64());
            WriteRoundScores(
                $"down observation attempt {attempt + 1}",
                scoringBaseline,
                red.TeamId,
                blue.TeamId);
            downObserved =
                scoringBaseline[red.TeamId] - previous[red.TeamId] == -7
                && scoringBaseline[blue.TeamId] - previous[blue.TeamId] == -7;
        }
        await Assert.That(downObserved).IsTrue();

        await GetFixtureTextAsync(redRuntimeUrl, "cgi-bin/up", cancellationToken);
        await GetFixtureTextAsync(blueRuntimeUrl, "cgi-bin/up", cancellationToken);
        await Assert.That(await GetFixtureTextAsync(redRuntimeUrl, "cgi-bin/health", cancellationToken))
            .IsEqualTo("up");
        await Assert.That(await GetFixtureTextAsync(blueRuntimeUrl, "cgi-bin/health", cancellationToken))
            .IsEqualTo("up");
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
        await AssertSubmissionAsync(expired, result: "Wrong", failureCode: "FlagExpired");

        JsonElement finalLeaderboard = default;
        var recoveryBaseline = scoringBaseline;
        var recoveryObserved = false;
        for (var attempt = 0; attempt < 4 && !recoveryObserved; attempt++)
        {
            currentBlueFlag = await PollFixtureTextAsync(
                blueRuntimeUrl,
                "cgi-bin/flag",
                value => value.StartsWith("flag{", StringComparison.Ordinal)
                    && !string.Equals(value, currentBlueFlag, StringComparison.Ordinal),
                TimeSpan.FromSeconds(75),
                cancellationToken);
            finalLeaderboard = await PollLeaderboardWhereAsync(
                anonymous,
                competitionId,
                scores =>
                    scores.GetValueOrDefault(red.TeamId, long.MinValue)
                        != recoveryBaseline[red.TeamId]
                    && scores.GetValueOrDefault(blue.TeamId, long.MinValue)
                        != recoveryBaseline[blue.TeamId],
                "the next completed AWD recovery round",
                TimeSpan.FromSeconds(45),
                cancellationToken);
            var recovered = finalLeaderboard.GetProperty("teams").EnumerateArray()
                .ToDictionary(
                    item => item.GetProperty("teamId").GetGuid(),
                    item => item.GetProperty("totalScore").GetInt64());
            WriteRoundScores(
                $"recovery observation attempt {attempt + 1}",
                recovered,
                red.TeamId,
                blue.TeamId);
            recoveryObserved =
                recovered[red.TeamId] - recoveryBaseline[red.TeamId] == 11
                && recovered[blue.TeamId] - recoveryBaseline[blue.TeamId] == 11;
            recoveryBaseline = recovered;
        }
        await Assert.That(recoveryObserved).IsTrue();
        var redEntry = finalLeaderboard.GetProperty("teams").EnumerateArray()
            .Single(item => item.GetProperty("teamId").GetGuid() == red.TeamId);
        await Assert.That(redEntry.GetProperty("rank").GetInt32()).IsEqualTo(1);
        var successfulAttacks = redEntry.GetProperty("slots").EnumerateArray()
            .SelectMany(slot => slot.GetProperty("breakdown").EnumerateArray())
            .Where(item => item.GetProperty("kind").GetString() == "Attack")
            .Sum(item => item.GetProperty("successfulCount").GetInt32());
        await Assert.That(successfulAttacks).IsEqualTo(1);

        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Finished", cancellationToken);
        await PollJsonAsync(
            admin,
            $"/api/v1/admin/competitions/{competitionId}/runtimes?competitionChallengeId={competitionChallengeId}",
            value => value.GetProperty("items").GetArrayLength() == 2
                && value.GetProperty("items").EnumerateArray()
                    .All(item => item.GetProperty("state").GetString() == "Stopped"),
            TimeSpan.FromSeconds(90),
            cancellationToken);
    }

    private static object BuildDefinition(string runtimeImage, string checkerImage) =>
        new
        {
            mode = "Awd",
            runtime = new
            {
                kind = "Container",
                allocation = "PerTeam",
                image = runtimeImage,
                command = Array.Empty<string>(),
                environment = new Dictionary<string, string>(),
                labels = new Dictionary<string, string>(),
                portMappings = new[] { new { containerPort = 8080, hostPort = 0 } },
                security = new
                {
                    noNewPrivileges = true,
                    readonlyRootfs = true,
                    runAsNonRoot = true,
                    capDrop = Array.Empty<string>(),
                    capAdd = Array.Empty<string>()
                },
                flagEnvironmentVariableName = (string?)null,
                internalPorts = Array.Empty<int>(),
                egressPolicy = "Isolated",
                limits = new
                {
                    memoryBytes = 67_108_864L,
                    nanoCpus = 100_000_000L,
                    pidsLimit = 64L
                },
                operationTimeoutSeconds = 60,
                urlBindings = new[]
                {
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/",
                        exposure = "Participants",
                        containerPort = 8080,
                        serviceName = (string?)null,
                        vmId = (string?)null,
                        guestPort = (int?)null,
                        isControlCheck = false
                    }
                },
                flagSource = "AwdRotation"
            },
            checker = new
            {
                image = checkerImage,
                command = Array.Empty<string>(),
                environment = new Dictionary<string, string>(),
                timeoutSeconds = 15,
                targetServiceName = (string?)null
            },
            flagInjection = new
            {
                command = "printf '%s' '${FLAG}' > /dev/shm/flag",
                timeoutSeconds = 5,
                serviceName = (string?)null
            }
        };

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
            new { name = teamName, trackKey = "default" },
            HttpStatusCode.Created,
            cancellationToken);
        var teamId = team.GetProperty("id").GetGuid();
        await E2ELifecycle.SubmitTeamRegistrationAsync(
            client, competitionId, teamId, cancellationToken);
        return new(client, teamId);
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
        return accepted.GetProperty("gameplayFactId").GetGuid();
    }

    private static Task<JsonElement> PollSubmissionAsync(
        HttpClient client,
        Guid competitionId,
        Guid gameplayFactId,
        CancellationToken cancellationToken) =>
        PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/gameplay-facts/{gameplayFactId}",
            value => value.GetProperty("state").GetString() == "Completed",
            TimeSpan.FromSeconds(30),
            cancellationToken);

    private static async Task AssertSubmissionAsync(
        JsonElement submission,
        string result,
        string? failureCode)
    {
        await Assert.That(submission.GetProperty("result").GetString()).IsEqualTo(result);
        var failure = submission.GetProperty("failureCode");
        if (failureCode is null)
            await Assert.That(failure.ValueKind).IsEqualTo(JsonValueKind.Null);
        else
            await Assert.That(failure.GetString()).IsEqualTo(failureCode);
    }

    private static async Task AssertSingleTargetAsync(JsonElement response, Guid teamId)
    {
        var items = response.GetProperty("items");
        await Assert.That(items.GetArrayLength()).IsEqualTo(1);
        await Assert.That(items[0].GetProperty("teamId").GetGuid()).IsEqualTo(teamId);
        await Assert.That(items[0].GetProperty("accesses").GetArrayLength()).IsEqualTo(1);
    }

    private static string RequiredFirstUrl(JsonElement runtime) =>
        E2ELifecycle.FirstDirectAddress(runtime);

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
                var scores = last.GetProperty("teams").EnumerateArray()
                    .ToDictionary(
                        item => item.GetProperty("teamId").GetGuid(),
                        item => item.GetProperty("totalScore").GetInt64());
                if (expectedScores.All(expected =>
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

    private static async Task<JsonElement> PollLeaderboardWhereAsync(
        HttpClient client,
        Guid competitionId,
        Func<IReadOnlyDictionary<Guid, long>, bool> completed,
        string expectation,
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
                var scores = last.GetProperty("teams").EnumerateArray()
                    .ToDictionary(
                        item => item.GetProperty("teamId").GetGuid(),
                        item => item.GetProperty("totalScore").GetInt64());
                if (completed(scores))
                    return last;
            }
            else if (response.StatusCode != HttpStatusCode.Accepted)
            {
                throw await UnexpectedResponseAsync(response, HttpStatusCode.OK, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException(
            $"Leaderboard did not reach {expectation}. Last response: {last}");
    }

    private static void WriteRoundScores(
        string observation,
        IReadOnlyDictionary<Guid, long> scores,
        Guid redTeamId,
        Guid blueTeamId) =>
        Console.WriteLine(
            $"AWD {observation}: red={scores.GetValueOrDefault(redTeamId)}, "
            + $"blue={scores.GetValueOrDefault(blueTeamId)}");

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
