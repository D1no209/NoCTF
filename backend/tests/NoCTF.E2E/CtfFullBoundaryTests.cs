using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace NoCTF.E2E;

[Category("CtfE2E")]
public sealed class CtfFullBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(300_000)]
    public async Task Ctf_flow_crosses_http_worker_runner_minio_and_docker(
        CancellationToken cancellationToken)
    {
        var baseUrl = RequiredEnvironment("NOCTF_E2E_BASE_URL");
        var runtimeImage = RequiredEnvironment("NOCTF_E2E_RUNTIME_IMAGE");
        using var anonymous = CreateClient(baseUrl);
        await AssertSpaDocumentAsync(anonymous, "/", cancellationToken);
        await AssertSpaDocumentAsync(anonymous, "/login", cancellationToken);
        using (var unknownApi = await anonymous.GetAsync(
            "/api/v1/does-not-exist",
            cancellationToken))
        {
            await Assert.That(unknownApi.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        using var admin = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            "ctf-e2e-admin",
            RequiredEnvironment("NOCTF_E2E_ADMIN_PASSWORD"),
            cancellationToken));

        var now = DateTimeOffset.UtcNow;
        var competition = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/competitions",
            new
            {
                title = "CTF full-boundary E2E",
                description = "External process boundary verification",
                mode = 0,
                startTime = now.AddMinutes(-1),
                endTime = now.AddHours(1),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5
            },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionId = competition.GetProperty("id").GetGuid();

        var template = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/challenges",
            new
            {
                mode = 0,
                visibility = 0,
                title = "Injected Flag Runtime",
                description = "Reads a generated per-team Flag from a real Docker runtime.",
                direction = "Web",
                definitionJson = BuildContainerDefinition(runtimeImage)
            },
            HttpStatusCode.Created,
            cancellationToken);
        var templateId = template.GetProperty("id").GetGuid();

        var attachmentBytes = Encoding.UTF8.GetBytes("NoCTF CTF E2E attachment\n");
        using var upload = new MultipartFormDataContent();
        using var attachmentContent = new ByteArrayContent(attachmentBytes);
        attachmentContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        upload.Add(attachmentContent, "File", "ctf-e2e.txt");
        using var uploadResponse = await admin.PostAsync(
            $"/api/v1/admin/challenges/{templateId}/attachments",
            upload,
            cancellationToken);
        var attachment = await ReadExpectedJsonAsync(
            uploadResponse,
            HttpStatusCode.Created,
            cancellationToken);
        var attachmentId = attachment.GetProperty("id").GetGuid();

        var challenge = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/challenges",
            new { challengeId = templateId, baseScore = 500, order = 0 },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionChallengeId = challenge.GetProperty("id").GetGuid();
        var challengeRevision = challenge.GetProperty("revision").GetInt32();

        var configurationJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            points = new { initialPoints = 500, minimumPoints = 100, decayFactor = 10 },
            bloodRewards = new[] { new { policy = 0, value = 25m } },
            maxFlagAttempts = 5
        }, JsonOptions);
        var updatedConfiguration = await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration",
            new { expectedRevision = challengeRevision, json = configurationJson },
            HttpStatusCode.OK,
            cancellationToken);
        challengeRevision = updatedConfiguration.GetProperty("revision").GetInt32();
        await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}",
            new { baseScore = 500, order = 0, isPublished = true, expectedRevision = challengeRevision },
            HttpStatusCode.OK,
            cancellationToken);

        var hint = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints",
            new { content = "The Flag is injected through the FLAG environment variable.", cost = 50, publishedAt = now.AddMinutes(-1) },
            HttpStatusCode.Created,
            cancellationToken);
        var hintId = hint.GetProperty("id").GetGuid();

        var composeTemplate = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/challenges",
            new
            {
                mode = 0,
                visibility = 0,
                title = "Compose Injected Flag Runtime",
                description = "Reads a generated per-team Flag from a real multi-service Docker Compose runtime.",
                direction = "Web",
                definitionJson = BuildComposeDefinition(runtimeImage)
            },
            HttpStatusCode.Created,
            cancellationToken);
        var composeChallenge = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/challenges",
            new
            {
                challengeId = composeTemplate.GetProperty("id").GetGuid(),
                baseScore = 250,
                order = 1
            },
            HttpStatusCode.Created,
            cancellationToken);
        var composeCompetitionChallengeId = composeChallenge.GetProperty("id").GetGuid();
        var composeChallengeRevision = composeChallenge.GetProperty("revision").GetInt32();
        var composeConfigurationJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            points = new { initialPoints = 250, minimumPoints = 100, decayFactor = 10 },
            bloodRewards = Array.Empty<object>(),
            maxFlagAttempts = 5
        }, JsonOptions);
        var updatedComposeConfiguration = await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{composeCompetitionChallengeId}/configuration",
            new
            {
                expectedRevision = composeChallengeRevision,
                json = composeConfigurationJson
            },
            HttpStatusCode.OK,
            cancellationToken);
        composeChallengeRevision = updatedComposeConfiguration
            .GetProperty("revision")
            .GetInt32();
        await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{composeCompetitionChallengeId}",
            new
            {
                baseScore = 250,
                order = 1,
                isPublished = true,
                expectedRevision = composeChallengeRevision
            },
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

        await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/v1/auth/register",
            new { userName = "ctf-player", email = "player@ctf-e2e.test", password = "ctf-player-password" },
            HttpStatusCode.Created,
            cancellationToken);
        using var player = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            "ctf-player",
            "ctf-player-password",
            cancellationToken));
        var globalTeam = await SendJsonAsync(
            player,
            HttpMethod.Post,
            "/api/v1/teams",
            new { name = "Boundary Team" },
            HttpStatusCode.Created,
            cancellationToken);
        var globalTeamId = globalTeam.GetProperty("id").GetGuid();
        var captainId = globalTeam.GetProperty("captainId").GetGuid();

        await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/v1/auth/register",
            new
            {
                userName = "ctf-teammate",
                email = "teammate@ctf-e2e.test",
                password = "ctf-teammate-password"
            },
            HttpStatusCode.Created,
            cancellationToken);
        using var teammate = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            "ctf-teammate",
            "ctf-teammate-password",
            cancellationToken));
        var invitation = await SendWithoutBodyForJsonAsync(
            player,
            HttpMethod.Post,
            $"/api/v1/teams/{globalTeamId}/invitation-token/rotate",
            HttpStatusCode.OK,
            cancellationToken);
        var invitationToken = invitation.GetProperty("invitationToken").GetString()
            ?? throw new InvalidOperationException("Invitation rotation did not return a token.");
        await Assert.That(invitationToken.Length).IsEqualTo(32);
        await SendJsonAsync(
            teammate,
            HttpMethod.Post,
            "/api/v1/teams/join",
            new { invitationToken },
            HttpStatusCode.OK,
            cancellationToken);

        var team = await SendJsonAsync(
            player,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/team-registrations",
            new { teamId = globalTeamId },
            HttpStatusCode.Created,
            cancellationToken);
        var teamId = team.GetProperty("id").GetGuid();

        var captainTeam = await GetJsonAsync(
            player,
            $"/api/v1/competitions/{competitionId}/teams/me",
            cancellationToken);
        var teammateTeam = await GetJsonAsync(
            teammate,
            $"/api/v1/competitions/{competitionId}/teams/me",
            cancellationToken);
        await Assert.That(captainTeam.GetProperty("id").GetGuid()).IsEqualTo(teamId);
        await Assert.That(teammateTeam.GetProperty("id").GetGuid()).IsEqualTo(teamId);
        var memberIds = teammateTeam.GetProperty("memberIds")
            .EnumerateArray()
            .Select(item => item.GetGuid())
            .ToArray();
        await Assert.That(memberIds).Contains(captainId);
        await Assert.That(memberIds.Length).IsEqualTo(2);
        var teammateId = memberIds.Single(id => id != captainId);

        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/start",
            HttpStatusCode.NoContent,
            cancellationToken);

        var listedAttachments = await GetJsonAsync(
            player,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments",
            cancellationToken);
        await Assert.That(listedAttachments.GetProperty("items").GetArrayLength()).IsEqualTo(1);
        var downloadedAttachment = await player.GetByteArrayAsync(
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments/{attachmentId}",
            cancellationToken);
        await Assert.That(downloadedAttachment).IsEquivalentTo(attachmentBytes);

        Guid? runtimeInstanceId = null;
        Guid? composeRuntimeInstanceId = null;
        try
        {
            var runtimeAccepted = await SendWithoutBodyForJsonAsync(
                player,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start",
                HttpStatusCode.Accepted,
                cancellationToken);
            runtimeInstanceId = runtimeAccepted.GetProperty("runtimeInstanceId").GetGuid();
            var runtime = await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime",
                value => value.GetProperty("state").GetInt32() == 2,
                TimeSpan.FromSeconds(90),
                cancellationToken);
            var runtimeUrl = runtime.GetProperty("urls")[0].GetString()
                ?? throw new InvalidOperationException("The running runtime did not expose a URL.");
            var flag = await PollTextAsync(runtimeUrl, TimeSpan.FromSeconds(30), cancellationToken);
            await Assert.That(flag).StartsWith("flag{");

            var composeRuntimeAccepted = await SendWithoutBodyForJsonAsync(
                player,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{composeCompetitionChallengeId}/runtime/start",
                HttpStatusCode.Accepted,
                cancellationToken);
            composeRuntimeInstanceId = composeRuntimeAccepted
                .GetProperty("runtimeInstanceId")
                .GetGuid();
            var composeRuntime = await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/challenges/{composeCompetitionChallengeId}/runtime",
                value => value.GetProperty("state").GetInt32() == 2,
                TimeSpan.FromSeconds(90),
                cancellationToken);
            var composeRuntimeUrl = composeRuntime.GetProperty("urls")[0].GetString()
                ?? throw new InvalidOperationException(
                    "The running Compose runtime did not expose a URL.");
            var composeFlag = await PollTextAsync(
                composeRuntimeUrl,
                TimeSpan.FromSeconds(30),
                cancellationToken);
            await Assert.That(composeFlag).StartsWith("flag{");
            await Assert.That(composeFlag).IsNotEqualTo(flag);

            var wrongSubmissionAccepted = await SendJsonAsync(
                teammate,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
                new { flag = flag + "-wrong" },
                HttpStatusCode.Accepted,
                cancellationToken);
            var wrongSubmissionId = wrongSubmissionAccepted
                .GetProperty("submissionId")
                .GetGuid();
            var wrongSubmission = await PollJsonAsync(
                teammate,
                $"/api/v1/competitions/{competitionId}/submissions/{wrongSubmissionId}",
                value => value.GetProperty("evaluationState").GetInt32() == 3,
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await Assert.That(wrongSubmission.GetProperty("result").GetInt32()).IsEqualTo(1);
            var unsolvedLeaderboard = await PollLeaderboardAsync(
                anonymous,
                competitionId,
                teamId,
                expectedScore: 0,
                cancellationToken);
            await AssertCtfLeaderboardStateAsync(
                unsolvedLeaderboard,
                teamId,
                competitionChallengeId,
                expectedSolveCount: 0,
                expectedBloodCount: 0);

            var submissionAccepted = await SendJsonAsync(
                player,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
                new { flag },
                HttpStatusCode.Accepted,
                cancellationToken);
            var submissionId = submissionAccepted.GetProperty("submissionId").GetGuid();
            var submission = await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/submissions/{submissionId}",
                value => value.GetProperty("evaluationState").GetInt32() == 3,
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await Assert.That(submission.GetProperty("result").GetInt32()).IsEqualTo(0);
            var evaluatedVersion = submission.GetProperty("processingVersion").GetInt64();

            var solvedLeaderboard = await PollLeaderboardAsync(
                anonymous,
                competitionId,
                teamId,
                expectedScore: 525,
                cancellationToken);
            await AssertCtfLeaderboardStateAsync(
                solvedLeaderboard,
                teamId,
                competitionChallengeId,
                expectedSolveCount: 1,
                expectedBloodCount: 1);

            var teammateSubmissions = await PollTeamSubmissionsAsync(
                teammate,
                competitionId,
                items => items.Any(item =>
                    item.GetProperty("id").GetGuid() == submissionId
                    && item.GetProperty("result").GetInt32() == 0),
                TimeSpan.FromSeconds(60),
                cancellationToken);
            var sharedCorrectSubmission = teammateSubmissions.Single(item =>
                item.GetProperty("id").GetGuid() == submissionId);
            await Assert.That(sharedCorrectSubmission.GetProperty("teamId").GetGuid())
                .IsEqualTo(teamId);
            await Assert.That(sharedCorrectSubmission
                    .GetProperty("competitionChallengeId")
                    .GetGuid())
                .IsEqualTo(competitionChallengeId);
            await Assert.That(sharedCorrectSubmission.GetProperty("submittedByUserId").GetGuid())
                .IsEqualTo(captainId);

            var duplicateSubmissionAccepted = await SendJsonAsync(
                teammate,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
                new { flag },
                HttpStatusCode.Accepted,
                cancellationToken);
            var duplicateSubmissionId = duplicateSubmissionAccepted
                .GetProperty("submissionId")
                .GetGuid();
            var duplicateSubmission = await PollJsonAsync(
                teammate,
                $"/api/v1/competitions/{competitionId}/submissions/{duplicateSubmissionId}",
                value => value.GetProperty("evaluationState").GetInt32() == 3,
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await Assert.That(duplicateSubmission.GetProperty("result").GetInt32()).IsEqualTo(2);
            var submissionsAfterDuplicate = await PollTeamSubmissionsAsync(
                teammate,
                competitionId,
                items => items.Any(item =>
                        item.GetProperty("id").GetGuid() == submissionId
                        && item.GetProperty("result").GetInt32() == 0)
                    && items.Any(item =>
                        item.GetProperty("id").GetGuid() == duplicateSubmissionId
                        && item.GetProperty("result").GetInt32() == 2),
                TimeSpan.FromSeconds(60),
                cancellationToken);
            var sharedDuplicateSubmission = submissionsAfterDuplicate.Single(item =>
                item.GetProperty("id").GetGuid() == duplicateSubmissionId);
            await Assert.That(sharedDuplicateSubmission.GetProperty("submittedByUserId").GetGuid())
                .IsEqualTo(teammateId);
            var leaderboardAfterDuplicate = await PollLeaderboardAsync(
                anonymous,
                competitionId,
                teamId,
                expectedScore: 525,
                cancellationToken);
            await AssertCtfLeaderboardStateAsync(
                leaderboardAfterDuplicate,
                teamId,
                competitionChallengeId,
                expectedSolveCount: 1,
                expectedBloodCount: 1);

            var unlockedHint = await SendWithoutBodyForJsonAsync(
                player,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/unlock",
                HttpStatusCode.Created,
                cancellationToken);
            await Assert.That(unlockedHint.GetProperty("cost").GetInt64()).IsEqualTo(50);
            await PollLeaderboardAsync(
                anonymous,
                competitionId,
                teamId,
                expectedScore: 475,
                cancellationToken);

            await SendWithoutBodyForJsonAsync(
                admin,
                HttpMethod.Post,
                $"/api/v1/admin/competitions/{competitionId}/submissions/{submissionId}/rejudge",
                HttpStatusCode.Accepted,
                cancellationToken);
            await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/submissions/{submissionId}",
                value => value.GetProperty("evaluationState").GetInt32() == 3
                    && value.GetProperty("processingVersion").GetInt64() > evaluatedVersion,
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await PollLeaderboardAsync(
                anonymous,
                competitionId,
                teamId,
                expectedScore: 475,
                cancellationToken);
        }
        finally
        {
            foreach (var runtime in new[]
            {
                (ChallengeId: competitionChallengeId, RuntimeId: runtimeInstanceId),
                (ChallengeId: composeCompetitionChallengeId, RuntimeId: composeRuntimeInstanceId)
            })
            {
                if (runtime.RuntimeId is null)
                    continue;
                using var stop = await player.PostAsync(
                    $"/api/v1/competitions/{competitionId}/challenges/{runtime.ChallengeId}/runtime/stop",
                    null,
                    CancellationToken.None);
                if (stop.StatusCode == HttpStatusCode.Accepted)
                {
                    await PollJsonAsync(
                        player,
                        $"/api/v1/competitions/{competitionId}/challenges/{runtime.ChallengeId}/runtime",
                        value => value.GetProperty("state").GetInt32() == 4,
                        TimeSpan.FromSeconds(60),
                        CancellationToken.None);
                }
            }
        }
    }

    private static HttpClient CreateClient(string baseUrl, string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string BuildContainerDefinition(string runtimeImage) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            runtime = new
            {
                allocation = 1,
                definition = new
                {
                    kind = "container",
                    image = runtimeImage,
                    environment = new Dictionary<string, string>(),
                    labels = new Dictionary<string, string>(),
                    portMappings = new Dictionary<string, int> { ["8080"] = 0 },
                    security = SecureContainerPolicy(),
                    flagEnvironmentVariableName = "FLAG",
                    egressPolicy = 0
                },
                limits = new
                {
                    memoryBytes = 67_108_864,
                    nanoCpus = 100_000_000,
                    pidsLimit = 64
                },
                ttlSeconds = 300,
                operationTimeoutSeconds = 60,
                urlBindings = new[]
                {
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/",
                        exposure = 0,
                        containerPort = 8080
                    }
                },
                flagSource = 1
            }
        }, JsonOptions);

    private static string BuildComposeDefinition(string runtimeImage) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            runtime = new
            {
                allocation = 1,
                definition = new
                {
                    kind = "compose",
                    composeYaml = $$"""
                        services:
                          web:
                            image: "{{runtimeImage}}"
                          db:
                            image: busybox:1.37
                            command:
                              - sleep
                              - "300"
                        """,
                    serviceResources = new Dictionary<string, object>
                    {
                        ["web"] = new
                        {
                            memoryBytes = 67_108_864,
                            nanoCpus = 100_000_000,
                            pidsLimit = 64
                        },
                        ["db"] = new
                        {
                            memoryBytes = 67_108_864,
                            nanoCpus = 100_000_000,
                            pidsLimit = 64
                        }
                    },
                    environment = new Dictionary<string, string>(),
                    labels = new Dictionary<string, string>(),
                    flagEnvironmentVariables = new Dictionary<string, string>
                    {
                        ["web"] = "FLAG"
                    },
                    egressPolicy = 0
                },
                limits = new
                {
                    memoryBytes = 134_217_728,
                    nanoCpus = 200_000_000,
                    pidsLimit = 128
                },
                ttlSeconds = 300,
                operationTimeoutSeconds = 60,
                urlBindings = new[]
                {
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/",
                        exposure = 0,
                        containerPort = 8080,
                        serviceName = "web"
                    }
                },
                flagSource = 1
            }
        }, JsonOptions);

    private static object SecureContainerPolicy() => new
    {
        noNewPrivileges = true,
        readonlyRootfs = true,
        runAsNonRoot = true,
        capDrop = new[] { "ALL" },
        capAdd = Array.Empty<string>()
    };

    private static async Task AssertSpaDocumentAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.SendAsync(request, cancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
        await Assert.That(await response.Content.ReadAsStringAsync(cancellationToken))
            .Contains("<div id=\"app\"></div>");
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

    private static async Task SendJsonWithoutResponseAsync(
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
        if (response.StatusCode != expected)
            throw await UnexpectedResponseAsync(response, expected, cancellationToken);
    }

    private static async Task<JsonElement> SendWithoutBodyForJsonAsync(
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
            if (completed(last)) return last;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Polling {path} timed out. Last response: {last}");
    }

    private static async Task<IReadOnlyList<JsonElement>> PollTeamSubmissionsAsync(
        HttpClient client,
        Guid competitionId,
        Func<IReadOnlyList<JsonElement>, bool> completed,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/competitions/{competitionId}/submissions?limit=50";
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        IReadOnlyList<JsonElement> last = [];
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await GetJsonAsync(client, path, cancellationToken);
            last = response.GetProperty("items").EnumerateArray().ToArray();
            if (completed(last))
                return last;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException(
            $"Polling the current team's submissions timed out. Last count: {last.Count}.");
    }

    private static async Task<string> PollTextAsync(
        string url,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Runtime URL {url} did not become reachable.");
    }

    private static async Task<JsonElement> PollLeaderboardAsync(
        HttpClient client,
        Guid competitionId,
        Guid teamId,
        long expectedScore,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/competitions/{competitionId}/leaderboard";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var leaderboard = await ReadExpectedJsonAsync(response, HttpStatusCode.OK, cancellationToken);
                var entry = leaderboard.GetProperty("entries").EnumerateArray()
                    .FirstOrDefault(item => item.GetProperty("teamId").GetGuid() == teamId);
                if (entry.ValueKind != JsonValueKind.Undefined
                    && entry.GetProperty("score").GetInt64() == expectedScore
                    && !leaderboard.GetProperty("stale").GetBoolean())
                    return leaderboard;
            }
            else if (response.StatusCode != HttpStatusCode.Accepted)
            {
                throw await UnexpectedResponseAsync(response, HttpStatusCode.OK, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Leaderboard did not reach score {expectedScore} for team {teamId}.");
    }

    private static async Task AssertCtfLeaderboardStateAsync(
        JsonElement leaderboard,
        Guid teamId,
        Guid competitionChallengeId,
        int expectedSolveCount,
        int expectedBloodCount)
    {
        var entry = leaderboard.GetProperty("entries").EnumerateArray()
            .Single(item => item.GetProperty("teamId").GetGuid() == teamId);
        await Assert.That(entry.GetProperty("solveCount").GetInt32())
            .IsEqualTo(expectedSolveCount);
        var challengeSolves = entry.GetProperty("challenges").EnumerateArray()
            .Where(item =>
                item.GetProperty("competitionChallengeId").GetGuid()
                == competitionChallengeId)
            .Sum(item => item.GetProperty("solveCount").GetInt32());
        await Assert.That(challengeSolves).IsEqualTo(expectedSolveCount);
        var slotKey = $"challenge:{competitionChallengeId:N}";
        var bloodCount = leaderboard.GetProperty("bloods").EnumerateArray()
            .Count(item =>
                item.GetProperty("teamId").GetGuid() == teamId
                && item.GetProperty("slotKey").GetString() == slotKey
                && item.GetProperty("bloodRank").GetInt32() == 1);
        await Assert.That(bloodCount).IsEqualTo(expectedBloodCount);
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required for the external CTF E2E test.");
}
