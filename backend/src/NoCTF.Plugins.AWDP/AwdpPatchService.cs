using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Security;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWDP;

/// <summary>
/// Handles AWDP FixScript submission and validation. Successful validation changes
/// AWDP state only; points are awarded later by AWDP round settlement.
/// </summary>
public class AwdpPatchService(
    ApplicationDbContext db,
    IContainerManager containerManager,
    IStorageProvider storageProvider,
    IPatchArchiveValidator patchArchiveValidator,
    AwdpConfigResolver configResolver,
    AwdpStateService stateService,
    IConfiguration configuration,
    ILogger<AwdpPatchService> logger) : IAwdpPatchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AwdpPatchSubmitResult> SubmitPatchAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Stream patchArchive,
        string fileName,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);

        if (competition is null || competition.GameModeType != GameModeType.Awdp)
            return new AwdpPatchSubmitResult(false, "mode_unavailable");

        if (now < competition.StartTime)
            return new AwdpPatchSubmitResult(false, "competition_not_started");

        if (now > competition.EndTime)
            return new AwdpPatchSubmitResult(false, "competition_ended");

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.CompetitionId == competitionId, ct);

        if (challenge is null)
            return new AwdpPatchSubmitResult(false, "challenge_not_found");

        var config = await configResolver.ResolveAsync(competitionId, challengeId, ct);
        var state = await stateService.GetOrCreateAsync(competitionId, teamId, challengeId, ct);
        var gameBox = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(g =>
                g.CompetitionId == competitionId &&
                g.TeamId == teamId &&
                g.ChallengeId == challengeId, ct);

        if (gameBox?.ContainerInstanceId is null)
        {
            state.InstanceStatus = AwdpInstanceStatus.InstanceNotCreated;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return new AwdpPatchSubmitResult(
                false,
                "instance_required",
                DefenseAttempts: state.DefenseAttempts,
                MaxDefenseAttempts: config.MaxDefenseAttempts);
        }

        if (gameBox.ExpiresAt is not null && gameBox.ExpiresAt <= now)
        {
            state.InstanceStatus = AwdpInstanceStatus.InstanceExpired;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return new AwdpPatchSubmitResult(
                false,
                "instance_expired",
                DefenseAttempts: state.DefenseAttempts,
                MaxDefenseAttempts: config.MaxDefenseAttempts);
        }

        state.InstanceStatus = AwdpInstanceStatus.InstanceRunning;

        if (state.FixStatus == AwdpFixStatus.FixSuccess && !config.AllowDefenseAfterFixSuccess)
        {
            state.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return new AwdpPatchSubmitResult(
                false,
                "already_fixed",
                DefenseAttempts: state.DefenseAttempts,
                MaxDefenseAttempts: config.MaxDefenseAttempts);
        }

        if (state.DefenseAttempts >= config.MaxDefenseAttempts)
        {
            state.FixStatus = AwdpFixStatus.DefenseAttemptsExhausted;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return new AwdpPatchSubmitResult(
                false,
                "defense_attempts_exhausted",
                DefenseAttempts: state.DefenseAttempts,
                MaxDefenseAttempts: config.MaxDefenseAttempts);
        }

        state.DefenseAttempts += 1;
        state.DefenseRequestedAt ??= now;
        state.LastFixSubmittedAt = now;
        state.FixStatus = AwdpFixStatus.FixAuditing;
        state.UpdatedAt = now;

        var attemptNumber = state.DefenseAttempts;
        var validation = await patchArchiveValidator.ValidateAsync(patchArchive, fileName, config.FixEntry, ct);
        if (patchArchive.CanSeek)
            patchArchive.Position = 0;

        if (!validation.IsValid)
        {
            var rejectedSubmission = CreateSubmission(
                competitionId,
                teamId,
                challengeId,
                patchArchiveUrl: string.Empty,
                status: AwdpPatchStatus.Rejected,
                fixStatus: AwdpFixStatus.AuditFailed,
                attemptNumber,
                fileName,
                config.FixEntry,
                now);
            rejectedSubmission.ValidatedAt = now;
            rejectedSubmission.ValidationDetail = validation.Error ?? "Archive audit failed.";

            state.FixStatus = AwdpFixStatus.AuditFailed;
            state.LastValidationDetail = rejectedSubmission.ValidationDetail;
            state.UpdatedAt = now;

            db.AwdpPatchSubmissions.Add(rejectedSubmission);
            await db.SaveChangesAsync(ct);
            return new AwdpPatchSubmitResult(
                false,
                validation.Error ?? "invalid_archive",
                rejectedSubmission.Id,
                state.DefenseAttempts,
                config.MaxDefenseAttempts);
        }

        state.FixStatus = AwdpFixStatus.FixUploading;
        state.UpdatedAt = now;

        var storagePath = $"patches/{competitionId}/{teamId}/{challengeId}/{Guid.NewGuid():N}{GetArchiveExtension(fileName)}";
        var storageKey = await storageProvider.UploadAsync(storagePath, patchArchive, GetContentType(fileName), ct);
        var url = ResolveDownloadUrl(await storageProvider.GetUrlAsync(storageKey, ct));

        var submission = CreateSubmission(
            competitionId,
            teamId,
            challengeId,
            url,
            AwdpPatchStatus.Pending,
            AwdpFixStatus.FixUploading,
            attemptNumber,
            fileName,
            config.FixEntry,
            now);

        db.AwdpPatchSubmissions.Add(submission);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "AWDP FixScript submitted: submission={SubmissionId} team={TeamId} challenge={ChallengeId} attempt={Attempt}/{MaxAttempts}",
            submission.Id, teamId, challengeId, state.DefenseAttempts, config.MaxDefenseAttempts);

        return new AwdpPatchSubmitResult(
            true,
            "pending",
            submission.Id,
            state.DefenseAttempts,
            config.MaxDefenseAttempts);
    }

    public async Task ValidatePatchAsync(Guid submissionId, CancellationToken ct = default)
    {
        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == submissionId, ct);

        if (IsTerminal(submission.FixStatus))
        {
            logger.LogInformation("AWDP FixScript submission {SubmissionId} is already terminal; skipping.", submissionId);
            return;
        }

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == submission.ChallengeId && c.CompetitionId == submission.CompetitionId, ct);
        var config = await configResolver.ResolveAsync(submission.CompetitionId, submission.ChallengeId, ct);
        var state = await stateService.GetOrCreateAsync(
            submission.CompetitionId,
            submission.TeamId,
            submission.ChallengeId,
            ct);
        var gameBox = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g =>
                g.CompetitionId == submission.CompetitionId &&
                g.TeamId == submission.TeamId &&
                g.ChallengeId == submission.ChallengeId, ct);

        if (string.IsNullOrWhiteSpace(challenge.ContainerImage))
        {
            await CompleteValidationAsync(
                submission,
                state,
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.AuditFailed,
                AwdpServiceStatus.ServiceUnknown,
                "Challenge has no container image.",
                ct);
            return;
        }

        if (gameBox?.ContainerInstanceId is null)
        {
            await CompleteValidationAsync(
                submission,
                state,
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                "Instance is not running.",
                ct);
            return;
        }

        state.FixStatus = AwdpFixStatus.FixRunning;
        state.InstanceStatus = AwdpInstanceStatus.InstanceRunning;
        state.UpdatedAt = DateTime.UtcNow;
        submission.FixStatus = AwdpFixStatus.FixRunning;
        await db.SaveChangesAsync(ct);

        var sandboxResult = await RunFixScriptAsync(challenge, submission, config, ct);
        if (sandboxResult.Status is AwdpFixStatus.FixTimeout or AwdpFixStatus.FixScriptError)
        {
            await CompleteValidationAsync(
                submission,
                state,
                AwdpPatchStatus.Rejected,
                sandboxResult.Status,
                AwdpServiceStatus.ServiceUnknown,
                sandboxResult.Detail,
                ct);
            return;
        }

        submission.Status = AwdpPatchStatus.Applied;
        submission.FixStatus = AwdpFixStatus.FixChecking;
        state.FixStatus = AwdpFixStatus.FixChecking;
        state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var oldContainerId = gameBox.ContainerInstanceId;
        var patchedContainer = await CreatePatchedContainerAsync(challenge, submission, ct);
        gameBox.ContainerInstanceId = patchedContainer.ContainerId;
        gameBox.PortMappingsJson = JsonSerializer.Serialize(patchedContainer.PortMappings, JsonOptions);
        gameBox.ExpiresAt ??= DateTime.UtcNow.AddHours(2);
        gameBox.LastInstanceActionAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(oldContainerId))
            await DestroyContainerIdAsync(gameBox, oldContainerId, ct);

        var checkerPassed = await RunCheckerAsync(challenge, submission, ct);
        if (!checkerPassed)
        {
            await RollbackAsync(challenge, gameBox, patchedContainer, ct);
            await CompleteValidationAsync(
                submission,
                state,
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                "Checker failed - service is unavailable or business behavior is broken.",
                ct);
            return;
        }

        var expSucceeded = await RunExpAsync(challenge, submission, ct);
        if (expSucceeded)
        {
            await RollbackAsync(challenge, gameBox, patchedContainer, ct);
            await CompleteValidationAsync(
                submission,
                state,
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.FixFailed,
                AwdpServiceStatus.ServiceOk,
                "EXP succeeded - service is healthy but the vulnerability still exists.",
                ct);
            return;
        }

        await CompleteValidationAsync(
            submission,
            state,
            AwdpPatchStatus.Verified,
            AwdpFixStatus.FixSuccess,
            AwdpServiceStatus.ServiceOk,
            "Checker passed and EXP failed - FixScript verified for future round defense scoring.",
            ct);

        logger.LogInformation(
            "AWDP FixScript verified: submission={SubmissionId} team={TeamId} challenge={ChallengeId}; round scoring will award defense points.",
            submissionId, submission.TeamId, submission.ChallengeId);
    }

    private async Task<(AwdpFixStatus Status, string Detail)> RunFixScriptAsync(
        Challenge challenge,
        AwdpPatchSubmission submission,
        AwdpChallengeConfig config,
        CancellationToken ct)
    {
        var env = BuildPatchEnvironment(submission);
        var timeout = TimeSpan.FromSeconds(config.FixTimeoutSeconds);
        var sandboxConfig = new ContainerConfig(
            Image: challenge.ContainerImage!,
            Command: BuildFixScriptCommand(),
            EnvironmentVariables: env,
            Labels: BuildLabels(submission.CompetitionId, submission.TeamId, submission.ChallengeId),
            Ttl: timeout,
            Entrypoint: ["/bin/sh", "-c"]);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout.Add(TimeSpan.FromSeconds(10)));
            var result = await containerManager.RunContainerAsync(sandboxConfig, cts.Token);

            if (result.ExitCode == 0)
                return (AwdpFixStatus.FixChecking, "FixScript completed.");

            var detail = $"FixScript exited with code {result.ExitCode}. stderr={result.StdErr?.Trim()}";
            return result.ExitCode == 124
                ? (AwdpFixStatus.FixTimeout, detail)
                : (AwdpFixStatus.FixScriptError, detail);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (AwdpFixStatus.FixTimeout, "FixScript timed out.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AWDP FixScript sandbox failed for submission {SubmissionId}.", submission.Id);
            return (AwdpFixStatus.FixScriptError, $"Sandbox error: {ex.Message}");
        }
    }

    private async Task<ContainerInstance> CreatePatchedContainerAsync(
        Challenge challenge,
        AwdpPatchSubmission submission,
        CancellationToken ct)
    {
        var patchedContainerConfig = new ContainerConfig(
            Image: challenge.ContainerImage!,
            EnvironmentVariables: BuildPatchEnvironment(submission),
            Labels: BuildLabels(submission.CompetitionId, submission.TeamId, submission.ChallengeId),
            PortMappings: BuildPortMappings(challenge));

        return await containerManager.CreateContainerAsync(patchedContainerConfig, ct);
    }

    private async Task<bool> RunCheckerAsync(
        Challenge challenge,
        AwdpPatchSubmission submission,
        CancellationToken ct)
    {
        if (challenge.CheckerConfig?.Image is null)
            return true;

        var timeout = TimeSpan.FromSeconds(challenge.CheckerConfig.TimeoutSeconds ?? 30);
        var checkerConfig = new ContainerConfig(
            Image: challenge.CheckerConfig.Image,
            Command: challenge.CheckerConfig.Command,
            EnvironmentVariables: BuildProbeEnvironment(submission),
            Ttl: timeout);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout.Add(TimeSpan.FromSeconds(10)));
            var result = await containerManager.RunContainerAsync(checkerConfig, cts.Token);
            logger.LogDebug(
                "AWDP checker for submission {SubmissionId}: exit={ExitCode}.",
                submission.Id, result.ExitCode);
            return result.ExitCode == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AWDP checker failed for submission {SubmissionId}.", submission.Id);
            return false;
        }
    }

    private async Task<bool> RunExpAsync(
        Challenge challenge,
        AwdpPatchSubmission submission,
        CancellationToken ct)
    {
        if (challenge.CheckerConfig?.ExpImage is null)
            return false;

        var timeout = TimeSpan.FromSeconds(challenge.CheckerConfig.TimeoutSeconds ?? 60);
        var expConfig = new ContainerConfig(
            Image: challenge.CheckerConfig.ExpImage,
            Command: challenge.CheckerConfig.ExpCommand,
            EnvironmentVariables: BuildProbeEnvironment(submission),
            Ttl: timeout);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout.Add(TimeSpan.FromSeconds(10)));
            var result = await containerManager.RunContainerAsync(expConfig, cts.Token);
            logger.LogDebug(
                "AWDP EXP for submission {SubmissionId}: exit={ExitCode}.",
                submission.Id, result.ExitCode);
            return result.ExitCode == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AWDP EXP failed for submission {SubmissionId}; treating exploit as failed.", submission.Id);
            return false;
        }
    }

    private async Task RollbackAsync(
        Challenge challenge,
        AwdGameBox gameBox,
        ContainerInstance patchedContainer,
        CancellationToken ct)
    {
        try
        {
            await containerManager.DestroyContainerAsync(patchedContainer, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to destroy rejected patched AWDP container {ContainerId}.", patchedContainer.ContainerId);
        }

        var originalConfig = new ContainerConfig(
            Image: challenge.ContainerImage!,
            Labels: BuildLabels(gameBox.CompetitionId, gameBox.TeamId, gameBox.ChallengeId),
            PortMappings: BuildPortMappings(challenge));

        try
        {
            var restored = await containerManager.CreateContainerAsync(originalConfig, ct);
            gameBox.ContainerInstanceId = restored.ContainerId;
            gameBox.PortMappingsJson = JsonSerializer.Serialize(restored.PortMappings, JsonOptions);
            gameBox.LastInstanceActionAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to restore original AWDP container for challenge {ChallengeId}.", gameBox.ChallengeId);
        }
    }

    private async Task DestroyContainerIdAsync(AwdGameBox gameBox, string containerId, CancellationToken ct)
    {
        try
        {
            var instance = new ContainerInstance(
                Guid.NewGuid(),
                gameBox.CompetitionId,
                gameBox.TeamId,
                gameBox.ChallengeId,
                "docker",
                containerId,
                ReadPorts(gameBox.PortMappingsJson),
                "running",
                DateTime.UtcNow);
            await containerManager.DestroyContainerAsync(instance, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to destroy previous AWDP container {ContainerId}.", containerId);
        }
    }

    private async Task CompleteValidationAsync(
        AwdpPatchSubmission submission,
        AwdpTeamChallengeState state,
        AwdpPatchStatus patchStatus,
        AwdpFixStatus fixStatus,
        AwdpServiceStatus serviceStatus,
        string detail,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        submission.Status = patchStatus;
        submission.FixStatus = fixStatus;
        submission.ValidatedAt = now;
        submission.ValidationDetail = detail;

        state.FixStatus = fixStatus;
        state.ServiceStatus = serviceStatus;
        state.FixSucceededAt = fixStatus == AwdpFixStatus.FixSuccess ? now : state.FixSucceededAt;
        state.LastValidationDetail = detail;
        state.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
    }

    private static AwdpPatchSubmission CreateSubmission(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        string patchArchiveUrl,
        AwdpPatchStatus status,
        AwdpFixStatus fixStatus,
        int attemptNumber,
        string fileName,
        string fixEntry,
        DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            PatchArchiveUrl = patchArchiveUrl,
            Status = status,
            FixStatus = fixStatus,
            AttemptNumber = attemptNumber,
            FileName = fileName,
            FixEntry = fixEntry,
            SubmittedAt = now
        };

    private static Dictionary<string, string> BuildPatchEnvironment(AwdpPatchSubmission submission)
        => new()
        {
            ["PATCH_URL"] = submission.PatchArchiveUrl,
            ["PATCH_FILE_NAME"] = submission.FileName,
            ["FIX_ENTRY"] = submission.FixEntry
        };

    private static Dictionary<string, string> BuildProbeEnvironment(AwdpPatchSubmission submission)
        => new()
        {
            ["TARGET_HOST"] = $"gamebox-{submission.TeamId:N}-{submission.ChallengeId:N}",
            ["TARGET_PORT"] = "80",
            ["TEAM_ID"] = submission.TeamId.ToString()
        };

    private static Dictionary<string, string> BuildLabels(Guid competitionId, Guid teamId, Guid challengeId)
        => new()
        {
            ["competitionId"] = competitionId.ToString(),
            ["teamId"] = teamId.ToString(),
            ["challengeId"] = challengeId.ToString()
        };

    private string ResolveDownloadUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        var publicBaseUrl = FirstNonBlank(
            configuration["StorageProvider:PublicBaseUrl"],
            configuration["App:PublicBaseUrl"],
            configuration["NoCTF:PublicBaseUrl"]);
        if (publicBaseUrl is null)
        {
            logger.LogWarning(
                "AWDP FixScript archive URL '{Url}' is relative and no StorageProvider:PublicBaseUrl is configured.",
                url);
            return url;
        }

        return $"{publicBaseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static Dictionary<int, int>? BuildPortMappings(Challenge challenge)
        => challenge.ExposedPort is > 0
            ? new Dictionary<int, int> { [challenge.ExposedPort.Value] = 0 }
            : null;

    private static Dictionary<int, int> ReadPorts(string? portsJson)
    {
        if (string.IsNullOrWhiteSpace(portsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, int>>(portsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string BuildFixScriptCommand()
        => """
           set -eu
           archive=/tmp/noctf-fix-archive
           work=/tmp/noctf-fix
           rm -rf "$work"
           mkdir -p "$work"
           if command -v curl >/dev/null 2>&1; then
             curl -fsSL "$PATCH_URL" -o "$archive"
           elif command -v wget >/dev/null 2>&1; then
             wget -q "$PATCH_URL" -O "$archive"
           else
             echo "curl or wget is required to fetch FixScript archive" >&2
             exit 127
           fi
           case "$PATCH_FILE_NAME" in
             *.zip|*.ZIP) unzip -q "$archive" -d "$work" ;;
             *.tar.gz|*.tgz|*.TGZ) tar -xzf "$archive" -C "$work" ;;
             *) echo "unsupported FixScript archive type: $PATCH_FILE_NAME" >&2; exit 120 ;;
           esac
           cd "$work"
           if [ ! -f "$FIX_ENTRY" ]; then
             echo "missing FixScript entry: $FIX_ENTRY" >&2
             exit 121
           fi
           sh "$FIX_ENTRY"
           """;

    private static string GetArchiveExtension(string fileName)
    {
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return ".tar.gz";
        if (fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            return ".tgz";
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return ".zip";
        return ".archive";
    }

    private static string GetContentType(string fileName)
        => fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : "application/gzip";

    private static bool IsTerminal(AwdpFixStatus status)
        => status is AwdpFixStatus.FixSuccess
            or AwdpFixStatus.FixFailed
            or AwdpFixStatus.FixServiceError
            or AwdpFixStatus.FixScriptError
            or AwdpFixStatus.FixTimeout
            or AwdpFixStatus.AuditFailed
            or AwdpFixStatus.DefenseAttemptsExhausted;
}
