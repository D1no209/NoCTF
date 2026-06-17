using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWDP;

/// <summary>
/// Handles AWDP patch upload, sandbox validation, and container rollback.
/// </summary>
public class AwdpPatchService(
    ApplicationDbContext db,
    IContainerManager containerManager,
    IStorageProvider storageProvider,
    IScoreSignalEmitter scoreSignalEmitter,
    ILogger<AwdpPatchService> logger) : IAwdpPatchService
{
    /// <summary>
    /// Uploads the patch archive and creates a Pending submission record.
    /// </summary>
    public async Task<Guid> SubmitPatchAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Stream patchArchive,
        string fileName,
        CancellationToken ct = default)
    {
        var storagePath = $"patches/{competitionId}/{teamId}/{challengeId}/{Guid.NewGuid()}.tar.gz";
        var url = await storageProvider.UploadAsync(storagePath, patchArchive, "application/gzip", ct);

        var submission = new AwdpPatchSubmission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            PatchArchiveUrl = url,
            SubmittedAt = DateTime.UtcNow,
            Status = AwdpPatchStatus.Pending
        };

        db.AwdpPatchSubmissions.Add(submission);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Patch submitted: submission={SubmissionId} team={TeamId} challenge={ChallengeId}",
            submission.Id, teamId, challengeId);

        return submission.Id;
    }

    /// <summary>
    /// Validates a patch in a sandbox container:
    /// 1. Run sandbox container with patch.sh via PATCH_URL env var.
    /// 2. If patch.sh succeeds, recreate the game box container with PATCH_URL set.
    /// 3. Run checker on patched container.
    /// 4. Run EXP on patched container.
    /// 5. checker pass + EXP fail → Verified, award DefensePoints.
    /// 6. Any failure → Rejected, rollback container to original image.
    /// </summary>
    public async Task ValidatePatchAsync(Guid submissionId, CancellationToken ct = default)
    {
        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == submissionId, ct);

        if (submission.Status == AwdpPatchStatus.Verified)
        {
            logger.LogInformation("Patch submission {SubmissionId} is already verified; skipping validation.", submissionId);
            return;
        }

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == submission.ChallengeId, ct);

        var gameBox = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g =>
                g.CompetitionId == submission.CompetitionId &&
                g.TeamId == submission.TeamId &&
                g.ChallengeId == submission.ChallengeId, ct);

        if (challenge.ContainerImage is null)
        {
            logger.LogWarning("Challenge {ChallengeId} has no container image; rejecting patch.", challenge.Id);
            submission.Status = AwdpPatchStatus.Rejected;
            submission.ValidatedAt = DateTime.UtcNow;
            submission.ValidationDetail = "Challenge has no container image.";
            await db.SaveChangesAsync(ct);
            return;
        }

        // Step 1: Run patch.sh in a sandbox container using the same challenge image.
        // The image is expected to have wget/curl and sh available (or use alpine).
        var sandboxEnv = new Dictionary<string, string>
        {
            ["PATCH_URL"] = submission.PatchArchiveUrl
        };

        var sandboxConfig = new ContainerConfig(
            Image: challenge.ContainerImage,
            Command: "sh -c \"wget -q $PATCH_URL -O /tmp/patch.tar.gz && cd /app && tar -xzf /tmp/patch.tar.gz --strip-components=0 && sh patch.sh\"",
            EnvironmentVariables: sandboxEnv,
            Ttl: TimeSpan.FromSeconds(60)
        );

        ContainerRunResult sandboxResult;
        try
        {
            sandboxResult = await containerManager.RunContainerAsync(sandboxConfig, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sandbox container failed for submission {SubmissionId}.", submissionId);
            submission.Status = AwdpPatchStatus.Rejected;
            submission.ValidatedAt = DateTime.UtcNow;
            submission.ValidationDetail = $"Sandbox error: {ex.Message}";
            await db.SaveChangesAsync(ct);
            return;
        }

        if (sandboxResult.ExitCode != 0)
        {
            logger.LogInformation(
                "Patch script failed (exit={ExitCode}) for submission {SubmissionId}.",
                sandboxResult.ExitCode, submissionId);
            submission.Status = AwdpPatchStatus.Rejected;
            submission.ValidatedAt = DateTime.UtcNow;
            submission.ValidationDetail = $"patch.sh exited with code {sandboxResult.ExitCode}. stderr={sandboxResult.StdErr?.Trim()}";
            await db.SaveChangesAsync(ct);
            return;
        }

        submission.Status = AwdpPatchStatus.Applied;
        await db.SaveChangesAsync(ct);

        // Step 2: Recreate the game box container with PATCH_URL env var so the image applies the patch on startup.
        if (gameBox?.ContainerInstanceId is not null)
        {
            var oldInstance = new ContainerInstance(
                Guid.NewGuid(), submission.CompetitionId, submission.TeamId, submission.ChallengeId,
                "docker", gameBox.ContainerInstanceId,
                new Dictionary<int, int>(), "running", DateTime.UtcNow);

            try
            {
                await containerManager.DestroyContainerAsync(oldInstance, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to destroy old game box container {ContainerId}.", gameBox.ContainerInstanceId);
            }
        }

        // Create patched container with PATCH_URL in env
        var patchedEnv = new Dictionary<string, string>
        {
            ["PATCH_URL"] = submission.PatchArchiveUrl
        };

        var patchedContainerConfig = new ContainerConfig(
            Image: challenge.ContainerImage,
            EnvironmentVariables: patchedEnv
        );

        ContainerInstance patchedContainer;
        try
        {
            patchedContainer = await containerManager.CreateContainerAsync(patchedContainerConfig, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create patched container for submission {SubmissionId}.", submissionId);
            submission.Status = AwdpPatchStatus.Rejected;
            submission.ValidatedAt = DateTime.UtcNow;
            submission.ValidationDetail = $"Container creation failed: {ex.Message}";
            await db.SaveChangesAsync(ct);
            return;
        }

        if (gameBox is not null)
        {
            gameBox.ContainerInstanceId = patchedContainer.ContainerId;
        }

        // Step 3: Run checker on patched container
        bool checkerPassed = false;
        if (challenge.CheckerConfig?.Image is not null)
        {
            var targetHost = $"gamebox-{submission.TeamId:N}-{submission.ChallengeId:N}";
            var checkerEnv = new Dictionary<string, string>
            {
                ["TARGET_HOST"] = targetHost,
                ["TARGET_PORT"] = "80",
                ["TEAM_ID"] = submission.TeamId.ToString()
            };

            var checkerConfig = new ContainerConfig(
                Image: challenge.CheckerConfig.Image,
                Command: challenge.CheckerConfig.Command,
                EnvironmentVariables: checkerEnv,
                Ttl: TimeSpan.FromSeconds(challenge.CheckerConfig.TimeoutSeconds ?? 30)
            );

            try
            {
                var checkerResult = await containerManager.RunContainerAsync(checkerConfig, ct);
                checkerPassed = checkerResult.ExitCode == 0;
                logger.LogDebug(
                    "Checker for submission {SubmissionId}: exit={ExitCode} passed={Passed}",
                    submissionId, checkerResult.ExitCode, checkerPassed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Checker threw for submission {SubmissionId}.", submissionId);
                checkerPassed = false;
            }
        }
        else
        {
            // No checker configured — assume service is up
            checkerPassed = true;
        }

        // Step 4: Run EXP on patched container
        bool expFailed = false;
        if (challenge.CheckerConfig?.ExpImage is not null)
        {
            var targetHost = $"gamebox-{submission.TeamId:N}-{submission.ChallengeId:N}";
            var expEnv = new Dictionary<string, string>
            {
                ["TARGET_HOST"] = targetHost,
                ["TARGET_PORT"] = "80",
                ["TEAM_ID"] = submission.TeamId.ToString()
            };

            var expConfig = new ContainerConfig(
                Image: challenge.CheckerConfig.ExpImage,
                Command: challenge.CheckerConfig.ExpCommand,
                EnvironmentVariables: expEnv,
                Ttl: TimeSpan.FromSeconds(60)
            );

            try
            {
                var expResult = await containerManager.RunContainerAsync(expConfig, ct);
                // EXP exit code != 0 means exploit failed (patch is effective)
                expFailed = expResult.ExitCode != 0;
                logger.LogDebug(
                    "EXP for submission {SubmissionId}: exit={ExitCode} expFailed={ExpFailed}",
                    submissionId, expResult.ExitCode, expFailed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "EXP threw for submission {SubmissionId}.", submissionId);
                expFailed = false;
            }
        }
        else
        {
            // No EXP configured — assume patch is effective
            expFailed = true;
        }

        // Step 5: Determine outcome
        bool verified = checkerPassed && expFailed;

        if (verified)
        {
            submission.Status = AwdpPatchStatus.Verified;
            submission.ValidatedAt = DateTime.UtcNow;
            submission.ValidationDetail = "Checker passed and EXP failed — patch verified.";

            await scoreSignalEmitter.EmitAsync(new ScoreSignalCreate(
                CompetitionId: submission.CompetitionId,
                TeamId: submission.TeamId,
                SignalType: ScoreSignalTypes.PatchVerified,
                IdempotencyKey: $"awdp:{submission.Id:N}:verified",
                SubjectType: "challenge",
                SubjectId: submission.ChallengeId,
                PayloadJson: ScoringJson.Serialize(new { submissionId = submission.Id }),
                OccurredAt: submission.ValidatedAt), ct);

            logger.LogInformation(
                "Patch verified for submission {SubmissionId}; emitted patch verification score signal.",
                submissionId);
        }
        else
        {
            submission.Status = AwdpPatchStatus.Rejected;
            submission.ValidatedAt = DateTime.UtcNow;
            submission.ValidationDetail = checkerPassed
                ? "EXP succeeded — patch did not fix the vulnerability."
                : "Checker failed — service is down after patch.";

            logger.LogInformation(
                "Patch rejected for submission {SubmissionId}: checkerPassed={CheckerPassed} expFailed={ExpFailed}",
                submissionId, checkerPassed, expFailed);

            // Rollback: destroy patched container, recreate original (without PATCH_URL)
            try
            {
                await containerManager.DestroyContainerAsync(patchedContainer, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to destroy patched container during rollback.");
            }

            var originalConfig = new ContainerConfig(Image: challenge.ContainerImage);
            try
            {
                var restoredContainer = await containerManager.CreateContainerAsync(originalConfig, ct);
                if (gameBox is not null)
                    gameBox.ContainerInstanceId = restoredContainer.ContainerId;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to restore original container for submission {SubmissionId}.", submissionId);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
