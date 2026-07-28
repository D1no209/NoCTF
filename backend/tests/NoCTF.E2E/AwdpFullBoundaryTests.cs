using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace NoCTF.E2E;

[Category("AwdpE2E")]
public sealed class AwdpFullBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(420_000)]
    public async Task Awdp_flow_crosses_break_upload_disposable_target_patch_checker_and_rejudge(
        CancellationToken cancellationToken)
    {
        var baseUrl = RequiredEnvironment("NOCTF_E2E_BASE_URL");
        var targetImage = RequiredEnvironment("NOCTF_E2E_TARGET_IMAGE");
        var checkerImage = RequiredEnvironment("NOCTF_E2E_CHECKER_IMAGE");
        using var anonymous = CreateClient(baseUrl);
        using var admin = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            "awdp-e2e-admin",
            RequiredEnvironment("NOCTF_E2E_ADMIN_PASSWORD"),
            cancellationToken));

        var now = DateTimeOffset.UtcNow;
        var competition = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/competitions",
            new
            {
                title = "AWDP full-boundary E2E",
                description = "Break, archive, disposable target, patch, and checker verification",
                mode = 2,
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
            schemaVersion = 1,
            roundDurationSeconds = 300,
            @break = new { settlement = 0, points = 40 },
            fix = new { settlement = 0, points = 60 },
            violationPenalty = 19,
            serviceDownPenalty = 13,
            requireBreakBeforeFix = true,
            breakWrongPenalty = 7,
            fixFailurePenalty = 11,
            maxBreakSubmissions = 5,
            maxFixSubmissions = 5,
            evaluationDispatchMode = 0
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
                title = "Disposable Patch Target",
                description = "Accepts an isolated patch and reports its fixed state.",
                direction = "Pwn"
            },
            HttpStatusCode.Created,
            cancellationToken);
        var templateId = template.GetProperty("id").GetGuid();
        const string breakFlag = "flag{awdp-full-boundary-break}";
        await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/challenges/{templateId}/flags",
            new { flag = breakFlag },
            HttpStatusCode.Created,
            cancellationToken);

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
            schemaVersion = 1,
            requireBreakBeforeFix = true,
            maxBreakSubmissions = 5,
            maxFixSubmissions = 5,
            runtime = new
            {
                provider = 0,
                allocation = 1,
                definition = new
                {
                    kind = "container",
                    image = targetImage,
                    environment = new Dictionary<string, string>(),
                    labels = new Dictionary<string, string>(),
                    portMappings = new Dictionary<string, int>(),
                    security = new
                    {
                        noNewPrivileges = true,
                        readonlyRootfs = false,
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
                ttlSeconds = 120,
                operationTimeoutSeconds = 60,
                runnerPool = "awdp-e2e"
            },
            patchEntrypoint = "fix.sh",
            patchCommand = new[] { "/bin/sh", "{entrypoint}" },
            patchTimeoutSeconds = 10,
            checker = new
            {
                provider = 0,
                image = checkerImage,
                command = Array.Empty<string>(),
                environment = new Dictionary<string, string>(),
                timeoutSeconds = 20
            },
            targetPort = 8080,
            readyTimeoutSeconds = 10
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

        var player = await RegisterTeamAsync(
            anonymous,
            baseUrl,
            competitionId,
            cancellationToken);
        using var playerClient = player.Client;
        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/start",
            HttpStatusCode.NoContent,
            cancellationToken);

        using (var invalidPatch = new MultipartFormDataContent())
        using (var invalidContent = new ByteArrayContent(Encoding.UTF8.GetBytes("not-a-tar-gzip")))
        {
            invalidContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            invalidPatch.Add(invalidContent, "File", "invalid.tar.gz");
            using var invalidResponse = await playerClient.PostAsync(
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-upload",
                invalidPatch,
                cancellationToken);
            await Assert.That(invalidResponse.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        }

        var fixedArchive = CreatePatchArchive("#!/bin/sh\nset -eu\ntouch /dev/shm/fixed\n");
        var reusablePatchId = await UploadPatchAsync(
            playerClient,
            competitionId,
            competitionChallengeId,
            fixedArchive,
            "fixed.tar.gz",
            cancellationToken);
        var rejectedFix = await SendJsonAsync(
            playerClient,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/fix-submissions",
            new { patchUploadId = reusablePatchId },
            HttpStatusCode.Conflict,
            cancellationToken);
        await Assert.That(rejectedFix.GetProperty("code").GetString()).IsEqualTo("break_required");

        var wrongBreakId = await SubmitBreakAsync(
            playerClient,
            competitionId,
            competitionChallengeId,
            "flag{wrong-awdp-break}",
            cancellationToken);
        var wrongBreak = await PollCompletedSubmissionAsync(
            playerClient,
            competitionId,
            wrongBreakId,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await AssertSubmissionAsync(wrongBreak, kind: 1, result: 1, failureCode: null);

        var correctBreakId = await SubmitBreakAsync(
            playerClient,
            competitionId,
            competitionChallengeId,
            breakFlag,
            cancellationToken);
        var correctBreak = await PollCompletedSubmissionAsync(
            playerClient,
            competitionId,
            correctBreakId,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await AssertSubmissionAsync(correctBreak, kind: 1, result: 0, failureCode: null);

        var fixedSubmissionId = await SubmitFixAsync(
            playerClient,
            competitionId,
            competitionChallengeId,
            reusablePatchId,
            cancellationToken);
        var fixedSubmission = await PollCompletedSubmissionAsync(
            playerClient,
            competitionId,
            fixedSubmissionId,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await AssertSubmissionAsync(fixedSubmission, kind: 2, result: 0, failureCode: null);
        var fixedProcessingVersion = fixedSubmission.GetProperty("processingVersion").GetInt64();
        var firstRuntimes = await PollStoppedRuntimesAsync(
            admin,
            competitionId,
            competitionChallengeId,
            expectedCount: 1,
            cancellationToken);

        await PollLeaderboardAsync(
            anonymous,
            competitionId,
            player.TeamId,
            expectedScore: 93,
            TimeSpan.FromSeconds(30),
            cancellationToken);

        var failedArchive = CreatePatchArchive("#!/bin/sh\nexit 9\n");
        var failedPatchId = await UploadPatchAsync(
            playerClient,
            competitionId,
            competitionChallengeId,
            failedArchive,
            "failed.tar.gz",
            cancellationToken);
        var failedSubmissionId = await SubmitFixAsync(
            playerClient,
            competitionId,
            competitionChallengeId,
            failedPatchId,
            cancellationToken);
        var failedSubmission = await PollCompletedSubmissionAsync(
            playerClient,
            competitionId,
            failedSubmissionId,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await AssertSubmissionAsync(failedSubmission, kind: 2, result: 1, failureCode: 25);
        _ = await PollStoppedRuntimesAsync(
            admin,
            competitionId,
            competitionChallengeId,
            expectedCount: 2,
            cancellationToken);
        await PollLeaderboardAsync(
            anonymous,
            competitionId,
            player.TeamId,
            expectedScore: 82,
            TimeSpan.FromSeconds(30),
            cancellationToken);

        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/submissions/{fixedSubmissionId}/rejudge",
            HttpStatusCode.Accepted,
            cancellationToken);
        var rejudged = await PollJsonAsync(
            playerClient,
            $"/api/v1/competitions/{competitionId}/submissions/{fixedSubmissionId}",
            value => value.GetProperty("evaluationState").GetInt32() == 3
                && value.GetProperty("processingVersion").GetInt64() > fixedProcessingVersion,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await AssertSubmissionAsync(rejudged, kind: 2, result: 0, failureCode: null);
        var finalRuntimes = await PollStoppedRuntimesAsync(
            admin,
            competitionId,
            competitionChallengeId,
            expectedCount: 3,
            cancellationToken);
        var runtimeIds = finalRuntimes.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();
        await Assert.That(runtimeIds.Distinct().Count()).IsEqualTo(3);
        await Assert.That(firstRuntimes.GetProperty("items")[0].GetProperty("generation").GetInt32())
            .IsEqualTo(1);
        await Assert.That(finalRuntimes.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("generation").GetInt32() == 2)).IsEqualTo(1);

        await PollLeaderboardAsync(
            anonymous,
            competitionId,
            player.TeamId,
            expectedScore: 82,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/finish",
            HttpStatusCode.NoContent,
            cancellationToken);
        await PollDockerCleanupAsync(runtimeIds, TimeSpan.FromSeconds(60), cancellationToken);
    }

    private static async Task<TeamSession> RegisterTeamAsync(
        HttpClient anonymous,
        string baseUrl,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/v1/auth/register",
            new
            {
                userName = "awdp-player",
                email = "player@awdp-e2e.test",
                password = "awdp-player-password"
            },
            HttpStatusCode.Created,
            cancellationToken);
        var client = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            "awdp-player",
            "awdp-player-password",
            cancellationToken));
        var team = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/teams",
            new { name = "Patch Team" },
            HttpStatusCode.Created,
            cancellationToken);
        return new(client, team.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> SubmitBreakAsync(
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

    private static async Task<Guid> SubmitFixAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid patchUploadId,
        CancellationToken cancellationToken)
    {
        var accepted = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/fix-submissions",
            new { patchUploadId },
            HttpStatusCode.Accepted,
            cancellationToken);
        return accepted.GetProperty("submissionId").GetGuid();
    }

    private static async Task<Guid> UploadPatchAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        byte[] archive,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(archive);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        form.Add(content, "File", fileName);
        using var response = await client.PostAsync(
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-upload",
            form,
            cancellationToken);
        var body = await ReadExpectedJsonAsync(response, HttpStatusCode.Created, cancellationToken);
        return body.GetProperty("patchUploadId").GetGuid();
    }

    private static byte[] CreatePatchArchive(string script)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        using (var data = new MemoryStream(Encoding.UTF8.GetBytes(script)))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "fix.sh")
            {
                DataStream = data
            };
            writer.WriteEntry(entry);
        }
        return output.ToArray();
    }

    private static Task<JsonElement> PollCompletedSubmissionAsync(
        HttpClient client,
        Guid competitionId,
        Guid submissionId,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/submissions/{submissionId}",
            value => value.GetProperty("evaluationState").GetInt32() == 3,
            timeout,
            cancellationToken);

    private static async Task AssertSubmissionAsync(
        JsonElement submission,
        int kind,
        int result,
        int? failureCode)
    {
        await Assert.That(submission.GetProperty("kind").GetInt32()).IsEqualTo(kind);
        await Assert.That(submission.GetProperty("result").GetInt32()).IsEqualTo(result);
        var failure = submission.GetProperty("failureCode");
        if (failureCode is null)
            await Assert.That(failure.ValueKind).IsEqualTo(JsonValueKind.Null);
        else
            await Assert.That(failure.GetInt32()).IsEqualTo(failureCode.Value);
    }

    private static Task<JsonElement> PollStoppedRuntimesAsync(
        HttpClient admin,
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedCount,
        CancellationToken cancellationToken) =>
        PollJsonAsync(
            admin,
            $"/api/v1/admin/competitions/{competitionId}/runtimes?competitionChallengeId={competitionChallengeId}",
            value => value.GetProperty("items").GetArrayLength() == expectedCount
                && value.GetProperty("items").EnumerateArray()
                    .All(item => item.GetProperty("state").GetInt32() == 4),
            TimeSpan.FromSeconds(90),
            cancellationToken);

    private static async Task PollLeaderboardAsync(
        HttpClient client,
        Guid competitionId,
        Guid teamId,
        long expectedScore,
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
                var entry = last.GetProperty("entries").EnumerateArray()
                    .SingleOrDefault(item => item.GetProperty("teamId").GetGuid() == teamId);
                if (!last.GetProperty("stale").GetBoolean()
                    && entry.ValueKind != JsonValueKind.Undefined
                    && entry.GetProperty("score").GetInt64() == expectedScore)
                    return;
            }
            else if (response.StatusCode != HttpStatusCode.Accepted)
            {
                throw await UnexpectedResponseAsync(response, HttpStatusCode.OK, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException(
            $"Leaderboard did not reach AWDP score {expectedScore}. Last response: {last}");
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
            if (!hasResources)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException("AWDP target, checker, or network resources were not cleaned up.");
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
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
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
            if (completed(last))
                return last;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Polling {path} timed out. Last response: {last}");
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required for the external AWDP E2E test.");

    private sealed record TeamSession(HttpClient Client, Guid TeamId);
}
