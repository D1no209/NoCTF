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
                mode = "Ctf",
                startTime = now.AddMinutes(10),
                endTime = now.AddHours(1),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5,
                maxConcurrentRuntimeInstancesPerTeam = 2
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
                mode = "Ctf",
                visibility = "Private",
                title = "Injected Flag Runtime",
                description = "Reads a generated per-team Flag from a real Docker runtime.",
                direction = "Web",
                definition = BuildContainerDefinition(runtimeImage)
            },
            HttpStatusCode.Created,
            cancellationToken);
        var templateId = template.GetProperty("id").GetGuid();

        var attachmentBytes = Encoding.UTF8.GetBytes("NoCTF CTF E2E attachment\n");
        using var upload = new MultipartFormDataContent();
        using var attachmentContent = new ByteArrayContent(attachmentBytes);
        attachmentContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        upload.Add(new StringContent("All"), "DeliveryPolicy");
        upload.Add(attachmentContent, "Files", "ctf-e2e.txt");
        using var uploadResponse = await admin.PostAsync(
            $"/api/v1/admin/challenges/{templateId}/attachments",
            upload,
            cancellationToken);
        var attachment = await ReadExpectedJsonAsync(
            uploadResponse,
            HttpStatusCode.Created,
            cancellationToken);
        var attachmentId = attachment.GetProperty("items")[0].GetProperty("id").GetGuid();

        var challenge = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/challenges",
            new { challengeId = templateId, order = 0 },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionChallengeId = challenge.GetProperty("id").GetGuid();

        var configuration = new
        {
            mode = "Ctf",
            ctf = new
            {
                scoreCurve = new { initialPoints = 500L, minimumPoints = 100L, decayTeamCount = 10, decayMode = "Quadratic", customExpression = (string?)null },
                bloodRewards = new[] { new { policy = "FixedPoints", value = 25m } },
                maxFlagAttempts = 5
            }
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
                mode = "Ctf",
                visibility = "Private",
                title = "Compose Injected Flag Runtime",
                description = "Reads a generated per-team Flag from a real multi-service Docker Compose runtime.",
                direction = "Web",
                definition = BuildComposeDefinition(runtimeImage)
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
                order = 1
            },
            HttpStatusCode.Created,
            cancellationToken);
        var composeCompetitionChallengeId = composeChallenge.GetProperty("id").GetGuid();
        var composeConfiguration = new
        {
            mode = "Ctf",
            ctf = new
            {
                scoreCurve = new { initialPoints = 250L, minimumPoints = 100L, decayTeamCount = 10, decayMode = "Quadratic", customExpression = (string?)null },
                bloodRewards = Array.Empty<object>(),
                maxFlagAttempts = 5
            }
        };
        await SendJsonAsync(
            admin,
            HttpMethod.Patch,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{composeCompetitionChallengeId}",
            new
            {
                presentation = new
                {
                    customTitle = (string?)null,
                    order = 1,
                    isPublished = true
                },
                rules = new { configuration = composeConfiguration }
            },
            HttpStatusCode.OK,
            cancellationToken);

        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Visible", cancellationToken);
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
        var team = await SendJsonAsync(
            player,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/teams",
            new { name = "Boundary Team", trackKey = "default" },
            HttpStatusCode.Created,
            cancellationToken);
        var teamId = team.GetProperty("id").GetGuid();
        var captainId = team.GetProperty("captainId").GetGuid();

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
        var originalInvitation = await GetJsonAsync(
            player,
            $"/api/v1/competitions/{competitionId}/teams/{teamId}/invitation-token",
            cancellationToken);
        var originalInvitationToken = originalInvitation
            .GetProperty("invitationToken")
            .GetString()
            ?? throw new InvalidOperationException("Invitation read did not return a token.");
        var invitation = await SendWithoutBodyForJsonAsync(
            player,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/teams/{teamId}/invitation-token/rotate",
            HttpStatusCode.OK,
            cancellationToken);
        var invitationToken = invitation.GetProperty("invitationToken").GetString()
            ?? throw new InvalidOperationException("Invitation rotation did not return a token.");
        await Assert.That(invitationToken.Length).IsEqualTo(32);
        await Assert.That(invitationToken).IsNotEqualTo(originalInvitationToken);
        var currentInvitation = await GetJsonAsync(
            player,
            $"/api/v1/competitions/{competitionId}/teams/{teamId}/invitation-token",
            cancellationToken);
        await Assert.That(currentInvitation.GetProperty("invitationToken").GetString())
            .IsEqualTo(invitationToken);
        await SendJsonWithoutResponseAsync(
            teammate,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/teams/join",
            new { invitationToken },
            HttpStatusCode.NoContent,
            cancellationToken);

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

        await E2ELifecycle.SubmitTeamRegistrationAsync(
            player, competitionId, teamId, cancellationToken);
        var badgeBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/YxQAAAAASUVORK5CYII=");
        using var badgeUpload = new MultipartFormDataContent();
        badgeUpload.Add(new StringContent("First solve"), "Name");
        badgeUpload.Add(new StringContent("Earned by the current team members."), "Description");
        using var badgeImage = new ByteArrayContent(badgeBytes);
        badgeImage.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        badgeUpload.Add(badgeImage, "Image", "first-solve.png");
        using var badgeResponse = await admin.PostAsync(
            $"/api/v1/admin/competitions/{competitionId}/badges",
            badgeUpload, cancellationToken);
        var badge = await ReadExpectedJsonAsync(
            badgeResponse, HttpStatusCode.Created, cancellationToken);
        var badgeId = badge.GetProperty("id").GetGuid();
        var challengeNodeId = Guid.NewGuid();
        var badgeNodeId = Guid.NewGuid();
        object[] graphNodes =
        [
            new
            {
                id = challengeNodeId, kind = 0,
                challenge = new { competitionChallengeId },
                requiresPrerequisites = true
            },
            new
            {
                id = badgeNodeId, kind = 1,
                badge = new { competitionBadgeId = badgeId },
                requiresPrerequisites = true
            }
        ];
        var graphEdges = new[]
        {
            new
            {
                id = Guid.NewGuid(), sourceNodeId = challengeNodeId,
                targetNodeId = badgeNodeId, condition = 0
            }
        };
        var savedGraph = await SendJsonAsync(
            admin, HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/progression",
            new
            {
                expectedConcurrencyStamp = (Guid?)null,
                enabled = true, showPlayerMap = true,
                nodes = graphNodes, edges = graphEdges
            },
            HttpStatusCode.OK, cancellationToken);
        await Assert.That(savedGraph.GetProperty("revision").GetInt64()).IsEqualTo(1);
        await E2ELifecycle.MakeScheduleDueAsync(admin, competitionId, cancellationToken);
        await E2ELifecycle.SetStatusAsync(
            admin, competitionId, "Published", cancellationToken);
        await E2ELifecycle.StartOrObserveRunningAsync(admin, competitionId, cancellationToken);

        var listedAttachments = await GetJsonAsync(
            player,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments",
            cancellationToken);
        await Assert.That(listedAttachments.GetProperty("items").GetArrayLength()).IsEqualTo(1);
        var downloadedAttachment = await player.GetByteArrayAsync(
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments/{attachmentId}",
            cancellationToken);
        await Assert.That(downloadedAttachment).IsEquivalentTo(attachmentBytes);

        using (var missingRuntime = await player.GetAsync(
                   $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/current",
                   cancellationToken))
        {
            await Assert.That(missingRuntime.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        var detailWithoutRuntime = await GetJsonAsync(
            player,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}",
            cancellationToken);
        await Assert.That(detailWithoutRuntime.GetProperty("id").GetGuid())
            .IsEqualTo(competitionChallengeId);
        await Assert.That(detailWithoutRuntime.GetProperty("hasRuntime").GetBoolean()).IsTrue();

        Guid? runtimeInstanceId = null;
        Guid? composeRuntimeInstanceId = null;
        try
        {
            var runtimeAccepted = await SendJsonAsync(
                player,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes",
                new { replacesRuntimeId = (Guid?)null },
                HttpStatusCode.Accepted,
                cancellationToken);
            runtimeInstanceId = runtimeAccepted.GetProperty("runtimeInstanceId").GetGuid();
            var runtime = await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/current",
                value => value.GetProperty("state").GetString() == "Running",
                TimeSpan.FromSeconds(90),
                cancellationToken);
            var runtimeUrl = E2ELifecycle.FirstDirectAddress(runtime);
            var flag = await PollTextAsync(runtimeUrl, TimeSpan.FromSeconds(30), cancellationToken);
            await Assert.That(flag).StartsWith("flag{");

            var composeRuntimeAccepted = await SendJsonAsync(
                player,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{composeCompetitionChallengeId}/runtimes",
                new { replacesRuntimeId = (Guid?)null },
                HttpStatusCode.Accepted,
                cancellationToken);
            composeRuntimeInstanceId = composeRuntimeAccepted
                .GetProperty("runtimeInstanceId")
                .GetGuid();
            var composeRuntime = await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/challenges/{composeCompetitionChallengeId}/runtimes/current",
                value => value.GetProperty("state").GetString() == "Running",
                TimeSpan.FromSeconds(90),
                cancellationToken);
            var composeRuntimeUrl = E2ELifecycle.FirstDirectAddress(composeRuntime);
            var composeFlag = await PollTextAsync(
                composeRuntimeUrl,
                TimeSpan.FromSeconds(30),
                cancellationToken);
            await Assert.That(composeFlag).StartsWith("flag{");
            await Assert.That(composeFlag).IsNotEqualTo(flag);

            var wrongGameplayFactAccepted = await SendJsonAsync(
                teammate,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
                new { flag = flag + "-wrong" },
                HttpStatusCode.Accepted,
                cancellationToken);
            var wrongGameplayFactId = wrongGameplayFactAccepted
                .GetProperty("gameplayFactId")
                .GetGuid();
            var wrongSubmission = await PollJsonAsync(
                teammate,
                $"/api/v1/competitions/{competitionId}/gameplay-facts/{wrongGameplayFactId}",
                value => value.GetProperty("state").GetString() == "Completed",
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await Assert.That(wrongSubmission.GetProperty("result").GetString()).IsEqualTo("Wrong");
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
            var gameplayFactId = submissionAccepted.GetProperty("gameplayFactId").GetGuid();
            var submission = await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/gameplay-facts/{gameplayFactId}",
                value => value.GetProperty("state").GetString() == "Completed",
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await Assert.That(submission.GetProperty("result").GetString()).IsEqualTo("Correct");
            var evaluatedAt = submission.GetProperty("updatedAt").GetDateTimeOffset();
            var activeProgression = await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/progression",
                value => value.GetProperty("badges").GetArrayLength() == 1,
                TimeSpan.FromSeconds(30), cancellationToken);
            await Assert.That(activeProgression.GetProperty("showPlayerMap").GetBoolean())
                .IsTrue();
            await Assert.That(activeProgression.GetProperty("badges")[0]
                .GetProperty("id").GetGuid()).IsEqualTo(badgeId);
            var captainProfile = await GetJsonAsync(
                anonymous, $"/api/v1/users/{captainId}", cancellationToken);
            var teammateProfile = await GetJsonAsync(
                anonymous, $"/api/v1/users/{teammateId}", cancellationToken);
            await Assert.That(captainProfile.GetProperty("badges").GetArrayLength())
                .IsEqualTo(1);
            await Assert.That(teammateProfile.GetProperty("badges").GetArrayLength())
                .IsEqualTo(1);
            await SendJsonAsync(
                admin, HttpMethod.Put,
                $"/api/v1/admin/competitions/{competitionId}/progression",
                new
                {
                    expectedConcurrencyStamp = savedGraph.GetProperty("concurrencyStamp").GetGuid(),
                    enabled = false, showPlayerMap = false,
                    nodes = graphNodes, edges = graphEdges
                },
                HttpStatusCode.OK, cancellationToken);
            var disabledProgression = await GetJsonAsync(
                player, $"/api/v1/competitions/{competitionId}/progression",
                cancellationToken);
            await Assert.That(disabledProgression.GetProperty("badges").GetArrayLength())
                .IsEqualTo(0);

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
                    item.GetProperty("id").GetGuid() == gameplayFactId
                    && item.GetProperty("result").GetString() == "Correct"),
                TimeSpan.FromSeconds(60),
                cancellationToken);
            var sharedCorrectSubmission = teammateSubmissions.Single(item =>
                item.GetProperty("id").GetGuid() == gameplayFactId);
            await Assert.That(sharedCorrectSubmission.GetProperty("teamId").GetGuid())
                .IsEqualTo(teamId);
            await Assert.That(sharedCorrectSubmission
                    .GetProperty("competitionChallengeId")
                    .GetGuid())
                .IsEqualTo(competitionChallengeId);
            await Assert.That(sharedCorrectSubmission.GetProperty("actorUserId").GetGuid())
                .IsEqualTo(captainId);

            var resubmittedGameplayFactAccepted = await SendJsonAsync(
                teammate,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
                new { flag },
                HttpStatusCode.Accepted,
                cancellationToken);
            var resubmittedGameplayFactId = resubmittedGameplayFactAccepted
                .GetProperty("gameplayFactId")
                .GetGuid();
            var resubmittedSubmission = await PollJsonAsync(
                teammate,
                $"/api/v1/competitions/{competitionId}/gameplay-facts/{resubmittedGameplayFactId}",
                value => value.GetProperty("state").GetString() == "Completed",
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await Assert.That(resubmittedSubmission.GetProperty("result").GetString()).IsEqualTo("Correct");
            var submissionsAfterResubmission = await PollTeamSubmissionsAsync(
                teammate,
                competitionId,
                items => items.Any(item =>
                        item.GetProperty("id").GetGuid() == gameplayFactId
                        && item.GetProperty("result").GetString() == "Correct")
                    && items.Any(item =>
                        item.GetProperty("id").GetGuid() == resubmittedGameplayFactId
                        && item.GetProperty("result").GetString() == "Correct"),
                TimeSpan.FromSeconds(60),
                cancellationToken);
            var sharedResubmittedSubmission = submissionsAfterResubmission.Single(item =>
                item.GetProperty("id").GetGuid() == resubmittedGameplayFactId);
            await Assert.That(sharedResubmittedSubmission.GetProperty("actorUserId").GetGuid())
                .IsEqualTo(teammateId);
            var leaderboardAfterResubmission = await PollLeaderboardAsync(
                anonymous,
                competitionId,
                teamId,
                expectedScore: 525,
                cancellationToken);
            await AssertCtfLeaderboardStateAsync(
                leaderboardAfterResubmission,
                teamId,
                competitionChallengeId,
                expectedSolveCount: 1,
                expectedBloodCount: 1);

            var unlockedHint = await SendWithoutBodyForJsonAsync(
                player,
                HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/unlock",
                HttpStatusCode.Accepted,
                cancellationToken);
            var hintFactId = unlockedHint.GetProperty("gameplayFactId").GetGuid();
            var hintFact = await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/gameplay-facts/{hintFactId}",
                value => value.GetProperty("state").GetString() == "Completed",
                TimeSpan.FromSeconds(60),
                cancellationToken);
            await Assert.That(hintFact.GetProperty("kind").GetString()).IsEqualTo("HintUnlock");
            await Assert.That(hintFact.GetProperty("result").GetString()).IsEqualTo("Unlocked");
            await Assert.That(hintFact.GetProperty("failureCode").ValueKind)
                .IsEqualTo(JsonValueKind.Null);
            await PollLeaderboardAsync(
                anonymous,
                competitionId,
                teamId,
                expectedScore: 475,
                cancellationToken);

            var rejudgeAccepted = await SendJsonAsync(
                admin,
                HttpMethod.Post,
                $"/api/v1/admin/competitions/{competitionId}/gameplay-fact-rejudgements",
                new { targetKind = "GameplayFact", targetId = gameplayFactId },
                HttpStatusCode.Accepted,
                cancellationToken);
            var rejudgeCutoff = rejudgeAccepted.GetProperty("cutoff").GetDateTimeOffset();
            await Assert.That(rejudgeCutoff).IsGreaterThanOrEqualTo(evaluatedAt);
            await PollJsonAsync(
                player,
                $"/api/v1/competitions/{competitionId}/gameplay-facts/{gameplayFactId}",
                value => value.GetProperty("state").GetString() == "Completed"
                    && value.GetProperty("updatedAt").GetDateTimeOffset() >= rejudgeCutoff,
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
                using var stop = await player.DeleteAsync(
                    $"/api/v1/competitions/{competitionId}/challenges/{runtime.ChallengeId}/runtimes/{runtime.RuntimeId}",
                    CancellationToken.None);
                if (stop.StatusCode == HttpStatusCode.Accepted)
                {
                    await PollJsonAsync(
                        player,
                        $"/api/v1/competitions/{competitionId}/challenges/{runtime.ChallengeId}/runtimes/current",
                        value => value.GetProperty("state").GetString() == "Stopped",
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

    private static object BuildContainerDefinition(string runtimeImage) =>
        new
        {
            mode = "Ctf",
            ctf = new { interactionKind = "FlagSubmission" },
            runtime = new
            {
                kind = "Container",
                allocation = "PerTeam",
                container = new
                {
                    image = runtimeImage,
                    command = Array.Empty<string>(),
                    environment = new Dictionary<string, string>(),
                    labels = new Dictionary<string, string>(),
                    portMappings = new[] { new { containerPort = 8080, hostPort = 0 } },
                    security = SecureContainerPolicy(),
                    flagEnvironmentVariableName = "FLAG",
                    internalPorts = Array.Empty<int>()
                },
                egressPolicy = "Isolated",
                limits = new
                {
                    memoryBytes = 67_108_864L,
                    nanoCpus = 100_000_000L,
                    pidsLimit = 64L
                },
                ttlSeconds = 300,
                operationTimeoutSeconds = 60,
                urlBindings = new[]
                {
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/",
                        exposure = "OwnerOnly",
                        containerPort = 8080,
                        serviceName = (string?)null,
                        vmId = (string?)null,
                        guestPort = (int?)null,
                        isControlCheck = false
                    }
                },
                flagSource = "PerTeam"
            }
        };

    private static object BuildComposeDefinition(string runtimeImage) =>
        new
        {
            mode = "Ctf",
            ctf = new { interactionKind = "FlagSubmission" },
            runtime = new
            {
                kind = "Compose",
                allocation = "PerTeam",
                compose = new
                {
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
                    serviceResources = new object[]
                {
                    new
                    {
                        serviceName = "web",
                        limits = new
                        {
                            memoryBytes = 67_108_864L,
                            nanoCpus = 100_000_000L,
                            pidsLimit = 64L
                        }
                    },
                    new
                    {
                        serviceName = "db",
                        limits = new
                        {
                            memoryBytes = 67_108_864L,
                            nanoCpus = 100_000_000L,
                            pidsLimit = 64L
                        }
                    }
                    },
                    environment = new Dictionary<string, string>(),
                    labels = new Dictionary<string, string>(),
                    flagEnvironmentVariables = new Dictionary<string, string> { ["web"] = "FLAG" }
                },
                egressPolicy = "Isolated",
                limits = new
                {
                    memoryBytes = 134_217_728L,
                    nanoCpus = 200_000_000L,
                    pidsLimit = 128L
                },
                ttlSeconds = 300,
                operationTimeoutSeconds = 60,
                urlBindings = new[]
                {
                    new
                    {
                        urlTemplate = "http://{HOST}:{PORT}/",
                        exposure = "OwnerOnly",
                        containerPort = 8080,
                        serviceName = "web",
                        vmId = (string?)null,
                        guestPort = (int?)null,
                        isControlCheck = false
                    }
                },
                flagSource = "PerTeam"
            }
        };

    private static object SecureContainerPolicy() => new
    {
        noNewPrivileges = true,
        readonlyRootfs = true,
        runAsNonRoot = true,
        capDrop = Array.Empty<string>(),
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
            .Contains("<div id=\"__nuxt\"></div>");
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
        E2ELifecycle.AddIdempotencyKey(request);
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
        E2ELifecycle.AddIdempotencyKey(request);
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
        var path = $"/api/v1/competitions/{competitionId}/gameplay-facts?limit=50";
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
                var entry = leaderboard.GetProperty("teams").EnumerateArray()
                    .FirstOrDefault(item => item.GetProperty("teamId").GetGuid() == teamId);
                if (entry.ValueKind != JsonValueKind.Undefined
                    && entry.GetProperty("totalScore").GetInt64() == expectedScore)
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
        _ = competitionChallengeId;
        var entry = leaderboard.GetProperty("teams").EnumerateArray()
            .Single(item => item.GetProperty("teamId").GetGuid() == teamId);
        var slots = entry.GetProperty("slots").EnumerateArray().ToArray();
        var solveCount = slots
            .SelectMany(slot => slot.GetProperty("breakdown").EnumerateArray())
            .Where(item => item.GetProperty("kind").GetString() == "Solve")
            .Sum(item => item.GetProperty("successfulCount").GetInt32());
        await Assert.That(solveCount).IsEqualTo(expectedSolveCount);
        var bloodCount = slots
            .SelectMany(slot => slot.GetProperty("entries").EnumerateArray())
            .Count(item => item.GetProperty("award").ValueKind == JsonValueKind.String
                && item.GetProperty("award").GetString() == "FirstBlood");
        await Assert.That(bloodCount).IsEqualTo(expectedBloodCount);
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required for the external CTF E2E test.");
}
