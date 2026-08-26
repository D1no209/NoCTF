using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoCTF.E2E;

[Category("AwdpE2E")]
public sealed class AwdpFullBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(420_000)]
    public async Task Awdp_v4_keeps_attack_runtime_fix_verification_and_round_scoring_independent(
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
                description = "Continuous Break and Fix scoring with per-team attack runtimes",
                mode = "Awdp",
                startTime = now.AddMinutes(10),
                endTime = now.AddHours(1),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5,
                maxConcurrentRuntimeInstancesPerTeam = 2
            },
            HttpStatusCode.Created,
            cancellationToken);
        var competitionId = competition.GetProperty("id").GetGuid();

        var competitionConfigurationJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            roundDurationSeconds = 30,
            @break = new
            {
                initialPoints = 40,
                minimumPoints = 20,
                decayTeamCount = 10,
                decayMode = 1
            },
            fix = new
            {
                initialPoints = 60,
                minimumPoints = 30,
                decayTeamCount = 10,
                decayMode = 1
            },
            serviceAbnormalPenalty = 13,
            requireBreakBeforeFix = false,
            flagWrongPenalty = 7,
            exploitSucceededPenalty = 11,
            maxBreakSubmissions = 5,
            maxFixSubmissions = 20,
            evaluationDispatchMode = 0
        }, JsonOptions);
        await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/configuration",
            new { json = competitionConfigurationJson },
            HttpStatusCode.OK,
            cancellationToken);

        var template = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/v1/admin/challenges",
            new
            {
                visibility = "Private",
                title = "Continuous AWDP Service",
                description = "Exposes an injected generation Flag and validates isolated Fix archives.",
                direction = "Pwn",
                mode = "Awdp",
                definitionJson = BuildDefinition(targetImage, checkerImage)
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

        var challengeConfigurationJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            requireBreakBeforeFix = false,
            maxBreakSubmissions = 5,
            maxFixSubmissions = 20,
            maximumPatchUploadBytes = 1_048_576,
            flagTemplate = new
            {
                header = "flag",
                bodyTemplate = "[GUID]",
                leetLiteralText = false
            }
        }, JsonOptions);
        await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration",
            new { json = challengeConfigurationJson },
            HttpStatusCode.OK,
            cancellationToken);
        await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}",
            new { customTitle = (string?)null, order = 0, isPublished = true },
            HttpStatusCode.OK,
            cancellationToken);

        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/make-visible",
            HttpStatusCode.NoContent,
            cancellationToken);
        var red = await RegisterTeamAsync(
            anonymous,
            baseUrl,
            competitionId,
            "red",
            cancellationToken);
        var blue = await RegisterTeamAsync(
            anonymous,
            baseUrl,
            competitionId,
            "blue",
            cancellationToken);
        var green = await RegisterTeamAsync(
            anonymous,
            baseUrl,
            competitionId,
            "green",
            cancellationToken);
        using var redClient = red.Client;
        using var blueClient = blue.Client;
        using var greenClient = green.Client;

        await AssertRuntimeStartRejectedAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            HttpStatusCode.NotFound,
            cancellationToken);

        await E2ELifecycle.MakeScheduleDueAsync(admin, competitionId, cancellationToken);
        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/publish",
            HttpStatusCode.NoContent,
            cancellationToken);
        await E2ELifecycle.StartOrObserveRunningAsync(admin, competitionId, cancellationToken);

        var blueDefenseTargetId = await RequestAndPollDefenseTargetAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            cancellationToken);
        await AssertPatchUploadRejectedAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            CreateEmptyArchive(),
            "empty.tar.gz",
            HttpStatusCode.UnprocessableEntity,
            cancellationToken);
        await AssertPatchUploadRejectedAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            CreateTarGzipArchive(("/fix.sh", "echo bad")),
            "absolute-path.tar.gz",
            HttpStatusCode.UnprocessableEntity,
            cancellationToken);
        await AssertPatchUploadRejectedAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            CreateTarGzipArchive(("../fix.sh", "echo bad")),
            "path-traversal.tar.gz",
            HttpStatusCode.UnprocessableEntity,
            cancellationToken);
        await AssertPatchUploadRejectedAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            CreateLinkArchive(TarEntryType.SymbolicLink),
            "symbolic-link.tar.gz",
            HttpStatusCode.UnprocessableEntity,
            cancellationToken);
        await AssertPatchUploadRejectedAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            CreateLinkArchive(TarEntryType.HardLink),
            "hard-link.tar.gz",
            HttpStatusCode.UnprocessableEntity,
            cancellationToken);
        await AssertPatchUploadRejectedAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            CreateTarGzipArchive(("fix.sh", "echo one"), ("fix.sh", "echo two")),
            "duplicate-path.tar.gz",
            HttpStatusCode.UnprocessableEntity,
            cancellationToken);
        await AssertPatchUploadRejectedAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            CreateRawTarArchive(("fix.sh", "echo ok")),
            "not-gzip.tar.gz",
            HttpStatusCode.UnprocessableEntity,
            cancellationToken);
        await AssertPatchUploadRejectedAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            CreateOversizedArchive(1_048_577),
            "oversized.tar.gz",
            HttpStatusCode.UnprocessableEntity,
            cancellationToken);

        var redRuntime = await StartAndPollRuntimeAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            cancellationToken);
        var blueRuntime = await StartAndPollRuntimeAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            cancellationToken);
        await Assert.That(redRuntime.GetProperty("id").GetGuid())
            .IsNotEqualTo(blueRuntime.GetProperty("id").GetGuid());
        await Assert.That(blueRuntime.GetProperty("id").GetGuid())
            .IsNotEqualTo(blueDefenseTargetId);
        var redUrl = redRuntime.GetProperty("urls")[0].GetString()
            ?? throw new InvalidOperationException("Red attack Runtime did not expose a URL.");
        var blueUrl = blueRuntime.GetProperty("urls")[0].GetString()
            ?? throw new InvalidOperationException("Blue attack Runtime did not expose a URL.");
        var redFlag = await PollTextAsync(new Uri(new Uri(redUrl), "flag").ToString(),
            TimeSpan.FromSeconds(30), cancellationToken);
        var blueFlag = await PollTextAsync(new Uri(new Uri(blueUrl), "flag").ToString(),
            TimeSpan.FromSeconds(30), cancellationToken);
        await Assert.That(redFlag).StartsWith("flag{");
        await Assert.That(blueFlag).StartsWith("flag{");
        await Assert.That(redFlag).IsNotEqualTo(blueFlag);

        var wrongBreakId = await SubmitBreakAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            "flag{wrong-awdp-break}",
            cancellationToken);
        var wrongBreak = await PollCompletedSubmissionAsync(
            redClient,
            competitionId,
            wrongBreakId,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await AssertSubmissionAsync(
            wrongBreak,
            kind: "BreakAttempt",
            result: "Wrong",
            failureCode: null);

        var foreignBreakId = await SubmitBreakAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            redFlag,
            cancellationToken);
        var foreignBreak = await PollCompletedSubmissionAsync(
            blueClient,
            competitionId,
            foreignBreakId,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await AssertSubmissionAsync(
            foreignBreak,
            kind: "BreakAttempt",
            result: "Wrong",
            failureCode: null);

        var redGenerationTwo = await ResetAndPollRuntimeAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            cancellationToken);
        await Assert.That(redGenerationTwo.GetProperty("id").GetGuid())
            .IsNotEqualTo(redRuntime.GetProperty("id").GetGuid());
        var redGenerationTwoUrl = redGenerationTwo.GetProperty("urls")[0].GetString()
            ?? throw new InvalidOperationException("Reset attack Runtime did not expose a URL.");
        var redGenerationTwoFlag = await PollTextAsync(
            new Uri(new Uri(redGenerationTwoUrl), "flag").ToString(),
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await Assert.That(redGenerationTwoFlag).IsNotEqualTo(redFlag);
        var expiredBreakId = await SubmitBreakAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            redFlag,
            cancellationToken);
        var expiredBreak = await PollCompletedSubmissionAsync(
            redClient,
            competitionId,
            expiredBreakId,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await AssertSubmissionAsync(
            expiredBreak,
            "BreakAttempt",
            "Wrong",
            "FlagExpired");

        var fixedArchive = CreatePatchArchive("#!/bin/sh\nset -eu\ntouch /dev/shm/fixed\n");
        var blueFixId = await UploadPatchAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            fixedArchive,
            "defense-succeeded.tar.gz",
            cancellationToken);
        var blueFix = await PollCompletedSubmissionAsync(
            blueClient,
            competitionId,
            blueFixId,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await AssertSubmissionAsync(
            blueFix,
            kind: "FixAttempt",
            result: "Correct",
            failureCode: null);
        var blueState = await PollDefenseTargetRecycledAsync(
            blueClient,
            competitionId,
            competitionChallengeId,
            blueDefenseTargetId,
            cancellationToken);
        await Assert.That(blueState.GetProperty("fixActivation").ValueKind)
            .IsEqualTo(JsonValueKind.Object);
        await Assert.That(blueState.GetProperty("breakActivation").ValueKind)
            .IsEqualTo(JsonValueKind.Null);

        await SubmitFixAndAssertAsync(
            greenClient,
            competitionId,
            competitionChallengeId,
            CreatePatchArchive("#!/bin/sh\nset -eu\n"),
            "exploit-succeeded.tar.gz",
            "FixAttempt",
            "Wrong",
            "AwdpExploitSucceeded",
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await SubmitFixAndAssertAsync(
            greenClient,
            competitionId,
            competitionChallengeId,
            CreatePatchArchive("#!/bin/sh\nset -eu\ntouch /dev/shm/service-abnormal-bypass\n"),
            "service-abnormal-bypass.tar.gz",
            "FixAttempt",
            "Rejected",
            "AwdpServiceAbnormal",
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await SubmitFixAndAssertAsync(
            greenClient,
            competitionId,
            competitionChallengeId,
            CreatePatchArchive("#!/bin/sh\nset -eu\ntouch /dev/shm/service-down\n"),
            "service-abnormal-down.tar.gz",
            "FixAttempt",
            "Rejected",
            "AwdpServiceAbnormal",
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await SubmitFixAndAssertAsync(
            greenClient,
            competitionId,
            competitionChallengeId,
            CreatePatchArchive("#!/bin/sh\nexit 9\n"),
            "nonzero.tar.gz",
            "FixAttempt",
            "Rejected",
            "AwdpPatchFailed",
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await SubmitFixAndAssertAsync(
            greenClient,
            competitionId,
            competitionChallengeId,
            CreatePatchArchive("#!/bin/sh\nsleep 30\n"),
            "timeout.tar.gz",
            "FixAttempt",
            "Rejected",
            "AwdpPatchTimeout",
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await SubmitFixAndAssertAsync(
            greenClient,
            competitionId,
            competitionChallengeId,
            CreateTarGzipArchive(("payload/readme.txt", "missing entrypoint\n")),
            "missing-fix-sh.tar.gz",
            "FixAttempt",
            "Rejected",
            "AwdpPatchFailed",
            TimeSpan.FromSeconds(90),
            cancellationToken);

        var correctBreakId = await SubmitBreakAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            redGenerationTwoFlag,
            cancellationToken);
        var correctBreak = await PollCompletedSubmissionAsync(
            redClient,
            competitionId,
            correctBreakId,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await AssertSubmissionAsync(correctBreak, "BreakAttempt", "Correct", null);
        var stoppedRedRuntime = await PollJsonAsync(
            redClient,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime",
            value => value.GetProperty("state").GetString() == "Stopped",
            TimeSpan.FromSeconds(60),
            cancellationToken);
        await Assert.That(stoppedRedRuntime.GetProperty("id").GetGuid())
            .IsEqualTo(redGenerationTwo.GetProperty("id").GetGuid());
        var redState = await PollJsonAsync(
            redClient,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state",
            value => value.GetProperty("breakActivation").ValueKind == JsonValueKind.Object,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        var redActivationRound = redState.GetProperty("breakActivation")
            .GetProperty("effectiveRound").GetInt32();
        var blueActivationRound = blueState.GetProperty("fixActivation")
            .GetProperty("effectiveRound").GetInt32();

        var failedArchive = CreatePatchArchive("#!/bin/sh\nexit 9\n");
        var redDefenseTargetId = await RequestAndPollDefenseTargetAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            cancellationToken);
        var redFailedFixId = await UploadPatchAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            redDefenseTargetId,
            failedArchive,
            "failed.tar.gz",
            cancellationToken);
        var redFailedFix = await PollCompletedSubmissionAsync(
            redClient,
            competitionId,
            redFailedFixId,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await AssertSubmissionAsync(
            redFailedFix,
            kind: "FixAttempt",
            result: "Rejected",
            failureCode: "AwdpPatchFailed");
        _ = await PollDefenseTargetRecycledAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            redDefenseTargetId,
            cancellationToken);

        var firstSettledScoreboard = await PollAwdpSettledScoreboardAsync(
            anonymous,
            competitionId,
            red.TeamId,
            blue.TeamId,
            green.TeamId,
            minimumSettledRounds: 3,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await AssertScoreboardArithmeticAsync(firstSettledScoreboard.Snapshot);
        await AssertNoSyntheticGlobalAdjustmentsAsync(firstSettledScoreboard.Snapshot);
        await AssertCurrentRoundScoresPendingAsync(firstSettledScoreboard);
        var firstSettledSlots = CaptureSettledSlots(firstSettledScoreboard.Snapshot);
        var firstSettledWindowEnd = firstSettledScoreboard.Schema
            .GetProperty("roundWindowEnd").GetInt32();
        var firstRedScore = Team(firstSettledScoreboard.Snapshot, red.TeamId)
            .GetProperty("totalScore").GetInt64();

        await AssertAwdpBreakJudgementAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            redGenerationTwoFlag,
            "Correct",
            cancellationToken);
        await AssertAwdpBreakJudgementAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            redFlag,
            "Wrong",
            cancellationToken);
        await AssertRuntimeStartRejectedAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            HttpStatusCode.Conflict,
            cancellationToken);
        var accumulatedScoreboard = await PollScoreboardAsync(
            anonymous,
            competitionId,
            observation => observation.Schema.GetProperty("rounds").EnumerateArray()
                    .Count(round => round.GetProperty("state").GetString() == "Settled")
                    > firstSettledScoreboard.Schema.GetProperty("rounds").EnumerateArray()
                        .Count(round => round.GetProperty("state").GetString() == "Settled")
                && Team(observation.Snapshot, red.TeamId).GetProperty("totalScore").GetInt64()
                    > firstRedScore,
            TimeSpan.FromSeconds(90),
            cancellationToken);
        await AssertSettledSlotsUnchangedAsync(firstSettledSlots, accumulatedScoreboard.Snapshot);
        await AssertScoreboardArithmeticAsync(accumulatedScoreboard.Snapshot);
        await AssertNoSyntheticGlobalAdjustmentsAsync(accumulatedScoreboard.Snapshot);
        await AssertCurrentRoundScoresPendingAsync(accumulatedScoreboard);

        var preBanRedScore = Team(accumulatedScoreboard.Snapshot, red.TeamId)
            .GetProperty("totalScore").GetInt64();
        var preBanSettledSlots = CaptureSettledSlots(accumulatedScoreboard.Snapshot)
            .Where(item => item.Key.TeamId == red.TeamId)
            .ToDictionary(item => item.Key, item => item.Value);
        await SendJsonWithoutResponseAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/teams/{red.TeamId}/ban",
            new { reason = "AWDP E2E leaderboard replay", announcePublicly = false },
            HttpStatusCode.NoContent,
            cancellationToken);
        _ = await PollScoreboardAsync(
            anonymous,
            competitionId,
            observation =>
            {
                var team = Team(observation.Snapshot, red.TeamId);
                return team.GetProperty("rankingState").GetString() == "Banned"
                    && team.GetProperty("totalScore").GetInt64() == 0
                    && team.GetProperty("slots").GetArrayLength() == 0;
            },
            TimeSpan.FromSeconds(45),
            cancellationToken);
        await SendJsonWithoutResponseAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/teams/{red.TeamId}/unban",
            new { },
            HttpStatusCode.NoContent,
            cancellationToken);
        var unbannedScoreboard = await PollScoreboardAsync(
            anonymous,
            competitionId,
            observation =>
            {
                var team = Team(observation.Snapshot, red.TeamId);
                return team.GetProperty("rankingState").GetString() == "Eligible"
                    && team.GetProperty("totalScore").GetInt64() >= preBanRedScore
                    && ContainsSettledSlots(preBanSettledSlots, observation.Snapshot);
            },
            TimeSpan.FromSeconds(45),
            cancellationToken);
        await AssertScoreboardArithmeticAsync(unbannedScoreboard.Snapshot);

        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/pause",
            HttpStatusCode.NoContent,
            cancellationToken);
        var pausedState = await GetJsonAsync(
            redClient,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state",
            cancellationToken);
        await Assert.That(pausedState.GetProperty("currentRound").GetInt32())
            .IsGreaterThanOrEqualTo(Math.Max(redActivationRound, blueActivationRound));
        await AssertAwdpRoundStableAsync(
            redClient,
            competitionId,
            competitionChallengeId,
            pausedState.GetProperty("currentRound").GetInt32(),
            TimeSpan.FromSeconds(11),
            cancellationToken);
        _ = await PollScoresSettledAsync(
            anonymous,
            competitionId,
            TimeSpan.FromSeconds(18),
            TimeSpan.FromSeconds(70),
            cancellationToken);
        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/resume",
            HttpStatusCode.NoContent,
            cancellationToken);
        var resumedScoreboard = await PollScoreboardAsync(
            anonymous,
            competitionId,
            observation => ReadProtocolInt64(observation.Snapshot, "version")
                > ReadProtocolInt64(accumulatedScoreboard.Snapshot, "version"),
            TimeSpan.FromSeconds(45),
            cancellationToken);
        await AssertScoreboardArithmeticAsync(resumedScoreboard.Snapshot);
        var resumedHistoricalWindow = await PollScoreboardAsync(
            anonymous,
            competitionId,
            observation => ContainsSettledSlots(firstSettledSlots, observation.Snapshot),
            TimeSpan.FromSeconds(45),
            cancellationToken,
            firstSettledWindowEnd);
        await AssertSettledSlotsUnchangedAsync(firstSettledSlots, resumedHistoricalWindow.Snapshot);
        await AssertScoreboardArithmeticAsync(resumedHistoricalWindow.Snapshot);

        await SendWithoutBodyAsync(
            admin,
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/finish",
            HttpStatusCode.NoContent,
            cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        var finishedScores = await ReadScoresAsync(anonymous, competitionId, cancellationToken);
        await AssertScoresStableAsync(
            anonymous,
            competitionId,
            finishedScores,
            TimeSpan.FromSeconds(7),
            cancellationToken);
    }

    private static string BuildDefinition(string targetImage, string checkerImage) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            runtime = new
            {
                allocation = 1,
                definition = new
                {
                    kind = "container",
                    image = targetImage,
                    environment = new Dictionary<string, string>(),
                    labels = new Dictionary<string, string>(),
                    portMappings = new Dictionary<string, int> { ["8080"] = 0 },
                    flagEnvironmentVariableName = "FLAG",
                    security = new
                    {
                        noNewPrivileges = true,
                        readonlyRootfs = false,
                        runAsNonRoot = true,
                        capDrop = new[] { "ALL" },
                        capAdd = Array.Empty<string>()
                    },
                    egressPolicy = 0,
                    internalPorts = new[] { 8080 }
                },
                limits = new
                {
                    memoryBytes = 67_108_864,
                    nanoCpus = 100_000_000,
                    pidsLimit = 64
                },
                ttlSeconds = 600,
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
            },
            patchEntrypoint = "fix.sh",
            patchCommand = new[] { "/bin/sh", "{entrypoint}" },
            patchTimeoutSeconds = 10,
            checker = new
            {
                image = checkerImage,
                command = Array.Empty<string>(),
                environment = new Dictionary<string, string>(),
                timeoutSeconds = 20
            },
            readyTimeoutSeconds = 10
        }, JsonOptions);

    private static async Task<TeamSession> RegisterTeamAsync(
        HttpClient anonymous,
        string baseUrl,
        Guid competitionId,
        string suffix,
        CancellationToken cancellationToken)
    {
        await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/v1/auth/register",
            new
            {
                userName = $"awdp-{suffix}",
                email = $"{suffix}@awdp-e2e.test",
                password = $"awdp-{suffix}-password"
            },
            HttpStatusCode.Created,
            cancellationToken);
        var client = CreateClient(baseUrl, await LoginAsync(
            anonymous,
            $"awdp-{suffix}",
            $"awdp-{suffix}-password",
            cancellationToken));
        var team = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/teams",
            new { name = $"{suffix} Team", trackKey = "default" },
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
        return accepted.GetProperty("gameplayFactId").GetGuid();
    }

    private static async Task<Guid> RequestAndPollDefenseTargetAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        var accepted = await SendWithoutBodyForJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets",
            HttpStatusCode.Accepted,
            cancellationToken);
        var runtimeInstanceId = accepted.GetProperty("runtimeInstanceId").GetGuid();
        _ = await PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state",
            value => value.GetProperty("defense") is var defense
                && defense.TryGetProperty("runtimeInstanceId", out var observedRuntimeId)
                && observedRuntimeId.ValueKind == JsonValueKind.String
                && observedRuntimeId.GetGuid() == runtimeInstanceId
                && defense.TryGetProperty("runtimeState", out var runtimeState)
                && runtimeState.GetString() == "Running",
            TimeSpan.FromSeconds(90),
            cancellationToken);
        return runtimeInstanceId;
    }

    private static async Task SubmitFixAndAssertAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        byte[] archive,
        string fileName,
        string kind,
        string result,
        string? failureCode,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var runtimeInstanceId = await RequestAndPollDefenseTargetAsync(
            client,
            competitionId,
            competitionChallengeId,
            cancellationToken);
        var factId = await UploadPatchAsync(
            client,
            competitionId,
            competitionChallengeId,
            runtimeInstanceId,
            archive,
            fileName,
            cancellationToken);
        var fact = await PollCompletedSubmissionAsync(
            client,
            competitionId,
            factId,
            timeout,
            cancellationToken);
        await AssertSubmissionAsync(fact, kind, result, failureCode);
        _ = await PollDefenseTargetRecycledAsync(
            client,
            competitionId,
            competitionChallengeId,
            runtimeInstanceId,
            cancellationToken);
    }

    private static Task<JsonElement> PollDefenseTargetRecycledAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        CancellationToken cancellationToken) =>
        PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state",
            value =>
            {
                var defense = value.GetProperty("defense");
                return defense.TryGetProperty("runtimeInstanceId", out var runtimeId)
                    && runtimeId.ValueKind == JsonValueKind.String
                    && runtimeId.GetGuid() == runtimeInstanceId
                    && defense.TryGetProperty("runtimeState", out var runtimeState)
                    && runtimeState.GetString() == "Stopped"
                    && defense.TryGetProperty("targetStoppedAt", out var targetStoppedAt)
                    && targetStoppedAt.ValueKind == JsonValueKind.String;
            },
            TimeSpan.FromSeconds(90),
            cancellationToken);

    private static async Task<Guid> UploadPatchAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        byte[] archive,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(archive);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        form.Add(content, "File", fileName);
        using var response = await client.PostAsync(
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets/{runtimeInstanceId}/fix",
            form,
            cancellationToken);
        var body = await ReadExpectedJsonAsync(response, HttpStatusCode.Accepted, cancellationToken);
        return body.GetProperty("gameplayFactId").GetGuid();
    }

    private static async Task AssertPatchUploadRejectedAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        byte[] archive,
        string fileName,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(archive);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        form.Add(content, "File", fileName);
        using var response = await client.PostAsync(
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets/{runtimeInstanceId}/fix",
            form,
            cancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    private static async Task<JsonElement> StartAndPollRuntimeAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        _ = await SendWithoutBodyForJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start",
            HttpStatusCode.Accepted,
            cancellationToken);
        return await PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime",
            value => value.GetProperty("state").GetString() == "Running",
            TimeSpan.FromSeconds(90),
            cancellationToken);
    }

    private static async Task<JsonElement> ResetAndPollRuntimeAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        var accepted = await SendWithoutBodyForJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/reset",
            HttpStatusCode.Accepted,
            cancellationToken);
        var replacementId = accepted.GetProperty("runtimeInstanceId").GetGuid();
        return await PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime",
            value => value.GetProperty("id").GetGuid() == replacementId
                && value.GetProperty("state").GetString() == "Running",
            TimeSpan.FromSeconds(90),
            cancellationToken);
    }

    private static async Task AssertAwdpBreakJudgementAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        string flag,
        string expectedResult,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-break-flag-judgement",
            new { flag },
            HttpStatusCode.OK,
            cancellationToken);
        await Assert.That(response.GetProperty("result").GetString())
            .IsEqualTo(expectedResult);
    }

    private static async Task AssertRuntimeStartRejectedAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start",
            content: null,
            cancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    private static byte[] CreatePatchArchive(string script)
    {
        return CreateTarGzipArchive(("fix.sh", script));
    }

    private static byte[] CreateEmptyArchive()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
        }
        return output.ToArray();
    }

    private static byte[] CreateTarGzipArchive(params (string Name, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
            WriteTarEntries(writer, files);
        return output.ToArray();
    }

    private static byte[] CreateRawTarArchive(params (string Name, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true))
            WriteTarEntries(writer, files);
        return output.ToArray();
    }

    private static byte[] CreateLinkArchive(TarEntryType entryType)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        using (var data = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n")))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh")
            {
                DataStream = data
            });
            writer.WriteEntry(new PaxTarEntry(entryType, "payload/link")
            {
                LinkName = "fix.sh"
            });
        }
        return output.ToArray();
    }

    private static byte[] CreateOversizedArchive(int bytes)
    {
        var payload = new byte[bytes];
        RandomNumberGenerator.Fill(payload);
        return payload;
    }

    private static void WriteTarEntries(
        TarWriter writer,
        params (string Name, string Content)[] files)
    {
        foreach (var file in files)
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(file.Content));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, file.Name)
            {
                DataStream = data
            });
        }
    }

    private static Task<JsonElement> PollCompletedSubmissionAsync(
        HttpClient client,
        Guid competitionId,
        Guid gameplayFactId,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        PollJsonAsync(
            client,
            $"/api/v1/competitions/{competitionId}/gameplay-facts/{gameplayFactId}",
            value => value.GetProperty("state").GetString() == "Completed",
            timeout,
            cancellationToken);

    private static async Task AssertSubmissionAsync(
        JsonElement submission,
        string kind,
        string result,
        string? failureCode)
    {
        await Assert.That(submission.GetProperty("kind").GetString()).IsEqualTo(kind);
        await Assert.That(submission.GetProperty("result").GetString()).IsEqualTo(result);
        var failure = submission.GetProperty("failureCode");
        if (failureCode is null)
            await Assert.That(failure.ValueKind).IsEqualTo(JsonValueKind.Null);
        else
            await Assert.That(failure.GetString()).IsEqualTo(failureCode);
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

    private static async Task AssertAwdpRoundStableAsync(
        HttpClient client,
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRound,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(duration);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await GetJsonAsync(
                client,
                $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state",
                cancellationToken);
            await Assert.That(state.GetProperty("currentRound").GetInt32()).IsEqualTo(expectedRound);
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }
    }

    private static async Task AssertScoresStableAsync(
        HttpClient client,
        Guid competitionId,
        IReadOnlyDictionary<Guid, long> expected,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(duration);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var actual = await ReadScoresAsync(client, competitionId, cancellationToken);
            await Assert.That(actual.OrderBy(item => item.Key).ToArray())
                .IsEquivalentTo(expected.OrderBy(item => item.Key).ToArray());
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }
    }

    private static async Task<IReadOnlyDictionary<Guid, long>> PollScoresSettledAsync(
        HttpClient client,
        Guid competitionId,
        TimeSpan stableFor,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        IReadOnlyDictionary<Guid, long>? last = null;
        var stableSince = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = await TryReadScoresAsync(client, competitionId, cancellationToken);
            if (current is not null)
            {
                if (last is not null && ScoresEqual(last, current))
                {
                    if (DateTimeOffset.UtcNow - stableSince >= stableFor)
                        return current;
                }
                else
                {
                    last = current;
                    stableSince = DateTimeOffset.UtcNow;
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }
        throw new TimeoutException($"AWDP leaderboard did not settle after pause. Last scores: {FormatScores(last)}");
    }

    private static async Task<IReadOnlyDictionary<Guid, long>> PollScoresEqualAsync(
        HttpClient client,
        Guid competitionId,
        IReadOnlyDictionary<Guid, long> expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        IReadOnlyDictionary<Guid, long>? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await TryReadScoresAsync(client, competitionId, cancellationToken);
            if (last is not null
                && last.OrderBy(item => item.Key).SequenceEqual(expected.OrderBy(item => item.Key)))
                return last;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException(
            $"AWDP leaderboard did not reach the frozen score. Last scores: {FormatScores(last)}");
    }

    private static async Task<ScoreboardObservation> PollAwdpSettledScoreboardAsync(
        HttpClient client,
        Guid competitionId,
        Guid attackTeamId,
        Guid defenseTeamId,
        Guid penaltyTeamId,
        int minimumSettledRounds,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await PollScoreboardAsync(
            client,
            competitionId,
            observation => observation.Schema.GetProperty("rounds").EnumerateArray()
                    .Count(round => round.GetProperty("state").GetString() == "Settled")
                    >= minimumSettledRounds
                && SettledBreakdowns(observation.Snapshot, attackTeamId, "Attack")
                    .Sum(item => item.GetProperty("earnedPoints").GetInt64()) > 0
                && SettledBreakdowns(observation.Snapshot, defenseTeamId, "Defense")
                    .Sum(item => item.GetProperty("earnedPoints").GetInt64()) > 0
                && SettledBreakdowns(observation.Snapshot, penaltyTeamId, "Defense")
                    .Sum(item => item.GetProperty("deductedPoints").GetInt64()) >= 37,
            timeout,
            cancellationToken);

    private static async Task<ScoreboardObservation> PollScoreboardAsync(
        HttpClient client,
        Guid competitionId,
        Func<ScoreboardObservation, bool> completed,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int? endingRound = null)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        ScoreboardObservation? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await TryReadScoreboardAsync(client, competitionId, cancellationToken, endingRound);
            if (last is not null && completed(last))
                return last;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException(
            $"AWDP scoreboard did not reach the expected round state. Last schema: {last?.Schema}; "
            + $"last snapshot: {last?.Snapshot}");
    }

    private static async Task<ScoreboardObservation?> TryReadScoreboardAsync(
        HttpClient client,
        Guid competitionId,
        CancellationToken cancellationToken,
        int? endingRound = null)
    {
        var query = endingRound is int round ? $"?endingRound={round}" : string.Empty;
        using var schemaResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/schema{query}",
            cancellationToken);
        if (schemaResponse.StatusCode == HttpStatusCode.Accepted)
            return null;
        var schema = await ReadExpectedJsonAsync(schemaResponse, HttpStatusCode.OK, cancellationToken);

        using var snapshotResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard{query}",
            cancellationToken);
        if (snapshotResponse.StatusCode == HttpStatusCode.Accepted)
            return null;
        var snapshot = await ReadExpectedJsonAsync(snapshotResponse, HttpStatusCode.OK, cancellationToken);
        if (!StringComparer.Ordinal.Equals(
                snapshot.GetProperty("schemaRevision").GetString(),
                schema.GetProperty("revision").GetString()))
            return null;
        return new(schema, snapshot);
    }

    private static long ReadProtocolInt64(JsonElement parent, string propertyName)
    {
        var value = parent.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String
            || !long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidOperationException(
                $"Protocol property '{propertyName}' must be a canonical Int64 decimal string.");
        return parsed;
    }

    private static JsonElement Team(JsonElement snapshot, Guid teamId) =>
        snapshot.GetProperty("teams").EnumerateArray()
            .Single(item => item.GetProperty("teamId").GetGuid() == teamId);

    private static IEnumerable<JsonElement> SettledBreakdowns(
        JsonElement snapshot,
        Guid teamId,
        string kind) =>
        Team(snapshot, teamId).GetProperty("slots").EnumerateArray()
            .Where(slot => slot.GetProperty("scoreState").GetString() == "Settled")
            .SelectMany(slot => slot.GetProperty("breakdown").EnumerateArray())
            .Where(item => item.GetProperty("kind").GetString() == kind);

    private static bool HasPendingEntry(
        ScoreboardObservation observation,
        Guid teamId,
        Guid entryId)
    {
        if (observation.Snapshot.GetProperty("currentRoundId").ValueKind != JsonValueKind.String)
            return false;
        var currentRoundId = observation.Snapshot.GetProperty("currentRoundId").GetGuid();
        var currentColumns = observation.Schema.GetProperty("columns").EnumerateArray()
            .Where(column => column.GetProperty("roundId").ValueKind == JsonValueKind.String
                && column.GetProperty("roundId").GetGuid() == currentRoundId)
            .Select(column => column.GetProperty("index").GetInt32())
            .ToHashSet();
        return Team(observation.Snapshot, teamId).GetProperty("slots").EnumerateArray()
            .Where(slot => currentColumns.Contains(slot.GetProperty("columnIndex").GetInt32()))
            .Any(slot => slot.GetProperty("scoreState").GetString() == "Pending"
                && slot.GetProperty("entries").EnumerateArray()
                    .Any(entry => entry.GetProperty("id").GetGuid() == entryId));
    }

    private static async Task AssertCurrentRoundScoresPendingAsync(ScoreboardObservation observation)
    {
        if (observation.Snapshot.GetProperty("currentRoundId").ValueKind != JsonValueKind.String)
            return;
        var currentRoundId = observation.Snapshot.GetProperty("currentRoundId").GetGuid();
        var currentColumns = observation.Schema.GetProperty("columns").EnumerateArray()
            .Where(column => column.GetProperty("roundId").ValueKind == JsonValueKind.String
                && column.GetProperty("roundId").GetGuid() == currentRoundId)
            .Select(column => column.GetProperty("index").GetInt32())
            .ToHashSet();
        foreach (var slot in observation.Snapshot.GetProperty("teams").EnumerateArray()
                     .SelectMany(team => team.GetProperty("slots").EnumerateArray())
                     .Where(slot => currentColumns.Contains(slot.GetProperty("columnIndex").GetInt32())))
        {
            await Assert.That(slot.GetProperty("scoreState").GetString()).IsEqualTo("Pending");
            await Assert.That(slot.GetProperty("earnedPoints").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(slot.GetProperty("deductedPoints").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(slot.GetProperty("netPoints").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(slot.GetProperty("entries").EnumerateArray().All(entry =>
                    entry.GetProperty("earnedPoints").ValueKind == JsonValueKind.Null
                    && entry.GetProperty("deductedPoints").ValueKind == JsonValueKind.Null
                    && entry.GetProperty("netPoints").ValueKind == JsonValueKind.Null))
                .IsTrue();
        }
    }

    private static async Task AssertScoreboardArithmeticAsync(JsonElement snapshot)
    {
        foreach (var team in snapshot.GetProperty("teams").EnumerateArray())
        {
            var slots = team.GetProperty("slots").EnumerateArray().ToArray();
            foreach (var slot in slots.Where(slot => slot.GetProperty("scoreState").GetString() != "Pending"))
            {
                var earned = slot.GetProperty("earnedPoints").GetInt64();
                var deducted = slot.GetProperty("deductedPoints").GetInt64();
                await Assert.That(slot.GetProperty("netPoints").GetInt64())
                    .IsEqualTo(checked(earned - deducted));
                await Assert.That(slot.GetProperty("breakdown").EnumerateArray()
                        .Sum(item => item.GetProperty("earnedPoints").GetInt64()))
                    .IsEqualTo(earned);
                await Assert.That(slot.GetProperty("breakdown").EnumerateArray()
                        .Sum(item => item.GetProperty("deductedPoints").GetInt64()))
                    .IsEqualTo(deducted);
            }
            var slotNet = slots
                .Where(slot => slot.GetProperty("netPoints").ValueKind == JsonValueKind.Number)
                .Sum(slot => slot.GetProperty("netPoints").GetInt64());
            var adjustments = team.GetProperty("globalAdjustments").EnumerateArray()
                .Sum(item => item.GetProperty("netPoints").GetInt64());
            await Assert.That(team.GetProperty("totalScore").GetInt64())
                .IsEqualTo(checked(
                    slotNet
                    + adjustments
                    + team.GetProperty("scoreOutsideWindow").GetInt64()));
        }
    }

    private static async Task AssertNoSyntheticGlobalAdjustmentsAsync(JsonElement snapshot)
    {
        foreach (var team in snapshot.GetProperty("teams").EnumerateArray())
            await Assert.That(team.GetProperty("globalAdjustments").GetArrayLength()).IsEqualTo(0);
    }

    private static IReadOnlyDictionary<(Guid TeamId, int ColumnIndex), SettledSlotScore> CaptureSettledSlots(
        JsonElement snapshot) =>
        snapshot.GetProperty("teams").EnumerateArray()
            .SelectMany(team => team.GetProperty("slots").EnumerateArray()
                .Where(slot => slot.GetProperty("scoreState").GetString() == "Settled")
                .Select(slot => new
                {
                    TeamId = team.GetProperty("teamId").GetGuid(),
                    ColumnIndex = slot.GetProperty("columnIndex").GetInt32(),
                    Score = new SettledSlotScore(
                        slot.GetProperty("earnedPoints").GetInt64(),
                        slot.GetProperty("deductedPoints").GetInt64(),
                        slot.GetProperty("netPoints").GetInt64())
                }))
            .ToDictionary(item => (item.TeamId, item.ColumnIndex), item => item.Score);

    private static async Task AssertSettledSlotsUnchangedAsync(
        IReadOnlyDictionary<(Guid TeamId, int ColumnIndex), SettledSlotScore> expected,
        JsonElement snapshot)
    {
        foreach (var item in expected)
        {
            var slot = Team(snapshot, item.Key.TeamId).GetProperty("slots").EnumerateArray()
                .Single(candidate => candidate.GetProperty("columnIndex").GetInt32()
                    == item.Key.ColumnIndex);
            await Assert.That(slot.GetProperty("scoreState").GetString()).IsEqualTo("Settled");
            await Assert.That(new SettledSlotScore(
                    slot.GetProperty("earnedPoints").GetInt64(),
                    slot.GetProperty("deductedPoints").GetInt64(),
                    slot.GetProperty("netPoints").GetInt64()))
                .IsEqualTo(item.Value);
        }
    }

    private static bool ContainsSettledSlots(
        IReadOnlyDictionary<(Guid TeamId, int ColumnIndex), SettledSlotScore> expected,
        JsonElement snapshot) =>
        expected.All(item => Team(snapshot, item.Key.TeamId).GetProperty("slots").EnumerateArray()
            .Any(slot => slot.GetProperty("columnIndex").GetInt32() == item.Key.ColumnIndex
                && slot.GetProperty("scoreState").GetString() == "Settled"
                && slot.GetProperty("earnedPoints").GetInt64() == item.Value.EarnedPoints
                && slot.GetProperty("deductedPoints").GetInt64() == item.Value.DeductedPoints
                && slot.GetProperty("netPoints").GetInt64() == item.Value.NetPoints));

    private static async Task<IReadOnlyDictionary<Guid, long>> ReadScoresAsync(
        HttpClient client,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var scores = await TryReadScoresAsync(client, competitionId, cancellationToken);
            if (scores is not null)
                return scores;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException("AWDP leaderboard did not become available.");
    }

    private static async Task<IReadOnlyDictionary<Guid, long>?> TryReadScoresAsync(
        HttpClient client,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted)
            return null;
        var leaderboard = await ReadExpectedJsonAsync(response, HttpStatusCode.OK, cancellationToken);
        return leaderboard.GetProperty("teams").EnumerateArray().ToDictionary(
            item => item.GetProperty("teamId").GetGuid(),
            item => item.GetProperty("totalScore").GetInt64());
    }

    private static string FormatScores(IReadOnlyDictionary<Guid, long>? scores) =>
        scores is null
            ? "unavailable"
            : string.Join(", ", scores.Select(item => $"{item.Key:N}={item.Value}"));

    private static bool ScoresEqual(
        IReadOnlyDictionary<Guid, long> left,
        IReadOnlyDictionary<Guid, long> right) =>
        left.Count == right.Count
        && left.OrderBy(item => item.Key).SequenceEqual(right.OrderBy(item => item.Key));

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
                    .All(item => item.GetProperty("state").GetString() == "Stopped"),
            TimeSpan.FromSeconds(90),
            cancellationToken);

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
    private sealed record ScoreboardObservation(JsonElement Schema, JsonElement Snapshot);
    private sealed record SettledSlotScore(long EarnedPoints, long DeductedPoints, long NetPoints);
}
