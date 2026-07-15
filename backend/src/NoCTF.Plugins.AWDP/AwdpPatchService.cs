using System.Text.Json;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Security;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Storage;
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
    ILogger<AwdpPatchService> logger,
    ICompetitionExecutionLease? executionLease = null) : IAwdpPatchService
{
    internal static readonly TimeSpan ContainerOperationCooldown = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly AwdpFixStatus[] TerminalFixStatuses =
    [
        AwdpFixStatus.FixSuccess,
        AwdpFixStatus.FixFailed,
        AwdpFixStatus.FixServiceError,
        AwdpFixStatus.FixScriptError,
        AwdpFixStatus.FixTimeout,
        AwdpFixStatus.AuditFailed,
        AwdpFixStatus.DefenseAttemptsExhausted,
        AwdpFixStatus.FixRuleViolation
    ];

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

        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft)
            return new AwdpPatchSubmitResult(false, "competition_not_started");

        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished)
            return new AwdpPatchSubmitResult(false, "competition_ended");

        if (competition.Status == CompetitionStatus.Paused)
            return new AwdpPatchSubmitResult(false, "competition_paused");

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == challengeId &&
                c.CompetitionId == competitionId &&
                !c.IsDeleting,
                ct);

        if (challenge is null)
            return new AwdpPatchSubmitResult(false, "challenge_not_found");

        await using var stateLock = await (executionLease ?? new CompetitionExecutionLease()).TryAcquireAsync(
            db,
            $"awdp-patch:{teamId:N}:{challengeId:N}",
            competitionId,
            ct);
        if (stateLock is null)
            return new AwdpPatchSubmitResult(false, "patch_in_progress");
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, stateLock.LostToken);
        ct = leaseCts.Token;
        Task SaveAndCommitAsync() => db.SaveChangesAsync(ct);

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
            await SaveAndCommitAsync();
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
            await SaveAndCommitAsync();
            return new AwdpPatchSubmitResult(
                false,
                "instance_expired",
                DefenseAttempts: state.DefenseAttempts,
                MaxDefenseAttempts: config.MaxDefenseAttempts);
        }

        state.InstanceStatus = AwdpInstanceStatus.InstanceRunning;

        if (IsContainerOperationCoolingDown(gameBox.LastInstanceActionAt, now))
        {
            state.UpdatedAt = now;
            await SaveAndCommitAsync();
            return new AwdpPatchSubmitResult(
                false,
                "instance_cooldown",
                DefenseAttempts: state.DefenseAttempts,
                MaxDefenseAttempts: config.MaxDefenseAttempts);
        }

        var validationInProgress = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == competitionId &&
                s.TeamId == teamId &&
                s.ChallengeId == challengeId &&
                !TerminalFixStatuses.Contains(s.FixStatus), ct);
        if (validationInProgress)
        {
            state.UpdatedAt = now;
            await SaveAndCommitAsync();
            return new AwdpPatchSubmitResult(
                false,
                "patch_in_progress",
                DefenseAttempts: state.DefenseAttempts,
                MaxDefenseAttempts: config.MaxDefenseAttempts);
        }

        if (state.FixStatus == AwdpFixStatus.FixSuccess && !config.AllowDefenseAfterFixSuccess)
        {
            state.UpdatedAt = now;
            await SaveAndCommitAsync();
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
            await SaveAndCommitAsync();
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
            await SaveAndCommitAsync();
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
        var submission = CreateSubmission(
            competitionId,
            teamId,
            challengeId,
            storageKey,
            AwdpPatchStatus.Pending,
            AwdpFixStatus.FixUploading,
            attemptNumber,
            fileName,
            config.FixEntry,
            now);

        db.AwdpPatchSubmissions.Add(submission);
        AddBackgroundTask(
            competitionId,
            AwdpBackgroundTaskTypes.PatchValidation,
            new AwdpPatchValidationPayload(submission.Id));
        try
        {
            await SaveAndCommitAsync();
        }
        catch
        {
            try
            {
                // SaveChanges can fail after the database committed. Queue the
                // object and let the cleanup worker re-check references instead
                // of directly deleting a potentially referenced archive.
                db.ChangeTracker.Clear();
                await StorageCleanupOutbox.EnqueueAsync(db, [storageKey], CancellationToken.None);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(
                    cleanupEx,
                    "Failed to queue orphaned AWDP FixScript archive {StorageKey} for cleanup.",
                    storageKey);
            }

            throw;
        }

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
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null)
        {
            logger.LogInformation(
                "AWDP FixScript submission {SubmissionId} no longer exists; skipping validation.",
                submissionId);
            return;
        }

        if (IsTerminal(submission.FixStatus))
        {
            logger.LogInformation("AWDP FixScript submission {SubmissionId} is already terminal; skipping.", submissionId);
            return;
        }

        var leaseProvider = executionLease ?? new CompetitionExecutionLease();
        await using var stateLock = await leaseProvider.TryAcquireAsync(
            db,
            $"awdp-patch:{submission.TeamId:N}:{submission.ChallengeId:N}",
            submission.CompetitionId,
            ct);
        if (stateLock is null)
            throw new InvalidOperationException($"AWDP patch {submissionId} is already being processed.");
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, stateLock.LostToken);
        ct = leaseCts.Token;
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == submission.ChallengeId &&
                c.CompetitionId == submission.CompetitionId &&
                !c.IsDeleting,
                ct);
        if (challenge is null)
        {
            submission.Status = AwdpPatchStatus.Rejected;
            submission.FixStatus = AwdpFixStatus.FixServiceError;
            submission.ValidatedAt = DateTime.UtcNow;
            submission.ValidationDetail = "Challenge is no longer available for patch validation.";
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "AWDP FixScript submission {SubmissionId} targets a removed challenge; validation was cancelled.",
                submissionId);
            return;
        }
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

        async Task CompleteAndCommitAsync(
            AwdpPatchStatus patchStatus,
            AwdpFixStatus fixStatus,
            AwdpServiceStatus serviceStatus,
            string detail,
            bool preserveSuccessfulState = false)
        {
            await CompleteValidationAsync(
                submission,
                state,
                patchStatus,
                fixStatus,
                serviceStatus,
                detail,
                ct,
                preserveSuccessfulState);
        }

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == submission.CompetitionId, ct);
        var validationNow = DateTime.UtcNow;
        if (competition is null ||
            competition.Status != CompetitionStatus.Running ||
            validationNow < competition.StartTime ||
            validationNow >= competition.EndTime)
        {
            await CompleteAndCommitAsync(
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceUnknown,
                "Competition is no longer accepting patch validation.");
            return;
        }

        if (string.IsNullOrWhiteSpace(challenge.ContainerImage))
        {
            await CompleteAndCommitAsync(
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.AuditFailed,
                AwdpServiceStatus.ServiceUnknown,
                "Challenge has no container image.");
            return;
        }

        if (gameBox?.ContainerInstanceId is null)
        {
            await CompleteAndCommitAsync(
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                "Instance is not running.");
            return;
        }

        if (gameBox.ExpiresAt is not null && gameBox.ExpiresAt <= validationNow)
        {
            state.InstanceStatus = AwdpInstanceStatus.InstanceExpired;
            await CompleteAndCommitAsync(
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                "Instance expired before patch validation started.");
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
            await CompleteAndCommitAsync(
                AwdpPatchStatus.Rejected,
                sandboxResult.Status,
                AwdpServiceStatus.ServiceUnknown,
                sandboxResult.Detail);
            return;
        }

        submission.Status = AwdpPatchStatus.Applied;
        submission.FixStatus = AwdpFixStatus.FixChecking;
        state.FixStatus = AwdpFixStatus.FixChecking;
        state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var expectedInstanceId = gameBox.ContainerInstanceId;
        ContainerInstance patchedContainer;
        BackgroundTaskItem candidateCleanupTask;
        await using (var candidateTransitionLock = await leaseProvider.TryAcquireAsync(
            db,
            CompetitionExecutionLeaseKeys.ChallengeInstance(submission.TeamId, submission.ChallengeId),
            submission.CompetitionId,
            ct))
        {
            if (candidateTransitionLock is null)
                throw new InvalidOperationException($"AWDP instance for patch {submissionId} is being transitioned.");

            await using var candidatePreparationLock = await leaseProvider.TryAcquireAsync(
                db,
                CompetitionExecutionLeaseKeys.RuntimePreparation,
                submission.CompetitionId,
                ct);
            if (candidatePreparationLock is null)
                throw new InvalidOperationException($"AWDP runtime for patch {submissionId} is being prepared or deleted.");

            using var candidateTransitionCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                candidateTransitionLock.LostToken,
                candidatePreparationLock.LostToken);
            var candidateCt = candidateTransitionCts.Token;
            await db.Entry(gameBox).ReloadAsync(candidateCt);
            var candidateNow = DateTime.UtcNow;
            var candidateAllowed = db.Entry(gameBox).State != EntityState.Detached &&
                !string.IsNullOrWhiteSpace(gameBox.ContainerInstanceId) &&
                string.Equals(gameBox.ContainerInstanceId, expectedInstanceId, StringComparison.Ordinal) &&
                (gameBox.ExpiresAt is null || gameBox.ExpiresAt > candidateNow) &&
                await db.Competitions.IgnoreQueryFilters().AsNoTracking().AnyAsync(c =>
                    c.Id == submission.CompetitionId &&
                    c.Status == CompetitionStatus.Running &&
                    c.StartTime <= candidateNow &&
                    c.EndTime > candidateNow, candidateCt) &&
                await db.Teams.IgnoreQueryFilters().AsNoTracking().AnyAsync(t =>
                    t.CompetitionId == submission.CompetitionId &&
                    t.Id == submission.TeamId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned, candidateCt) &&
                await db.Challenges.IgnoreQueryFilters().AsNoTracking().AnyAsync(c =>
                    c.CompetitionId == submission.CompetitionId &&
                    c.Id == submission.ChallengeId &&
                    !c.IsDeleting, candidateCt);
            if (!candidateAllowed)
            {
                await CompleteAndCommitAsync(
                    AwdpPatchStatus.Rejected,
                    AwdpFixStatus.FixServiceError,
                    AwdpServiceStatus.ServiceError,
                    "Instance changed or became unavailable before candidate creation.");
                return;
            }

            patchedContainer = await CreatePatchedContainerAsync(challenge, submission, gameBox.ExpiresAt, candidateCt);
            candidateCleanupTask = await GetOrCreateContainerCleanupSafeguardAsync(
                submission.CompetitionId,
                submission.Id,
                patchedContainer,
                candidateCt);
            try
            {
                // Persist a dormant cleanup saga before releasing the shared
                // instance transition lease. Deletion cleanup therefore sees
                // either no external candidate or its durable cleanup record.
                await db.SaveChangesAsync(candidateCt);
            }
            catch
            {
                db.Entry(candidateCleanupTask).State = EntityState.Detached;
                await DestroyCandidateBestEffortAsync(patchedContainer);
                throw;
            }
        }

        var checkResult = await RunCheckAsync(challenge, submission, patchedContainer, ct);
        if (checkResult.FixStatus != AwdpFixStatus.FixSuccess)
        {
            ActivateContainerCleanupTask(candidateCleanupTask, patchedContainer);
            var restoredPreviousSuccess = await FindPreviousSuccessfulPatchAsync(submission, ct) is not null;
            await CompleteAndCommitAsync(
                AwdpPatchStatus.Rejected,
                checkResult.FixStatus,
                checkResult.ServiceStatus,
                checkResult.Detail,
                preserveSuccessfulState: restoredPreviousSuccess);
            return;
        }

        // Patch validation owns the AWDP patch lease first. Acquire the shared
        // challenge-instance lease only for the final compare-and-swap so long
        // FixScript/checker runs do not block Start/Stop/Destroy. This lock
        // order is global for this flow and cannot deadlock with instance
        // transitions, which never acquire the AWDP patch lease.
        await using var transitionLock = await leaseProvider.TryAcquireAsync(
            db,
            CompetitionExecutionLeaseKeys.ChallengeInstance(submission.TeamId, submission.ChallengeId),
            submission.CompetitionId,
            ct);
        if (transitionLock is null)
            throw new InvalidOperationException($"AWDP instance for patch {submissionId} is being transitioned.");

        await using var transitionPreparationLock = await leaseProvider.TryAcquireAsync(
            db,
            CompetitionExecutionLeaseKeys.RuntimePreparation,
            submission.CompetitionId,
            ct);
        if (transitionPreparationLock is null)
            throw new InvalidOperationException($"AWDP runtime for patch {submissionId} is being prepared or deleted.");

        using var transitionCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            transitionLock.LostToken,
            transitionPreparationLock.LostToken);
        ct = transitionCts.Token;

        await db.Entry(gameBox).ReloadAsync(ct);
        var switchNow = DateTime.UtcNow;
        var competitionStillRunning = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c =>
                c.Id == submission.CompetitionId &&
                c.Status == CompetitionStatus.Running &&
                c.StartTime <= switchNow &&
                c.EndTime > switchNow, ct);
        var teamStillActive = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t =>
                t.CompetitionId == submission.CompetitionId &&
                t.Id == submission.TeamId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned, ct);
        var challengeStillActive = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c =>
                c.CompetitionId == submission.CompetitionId &&
                c.Id == submission.ChallengeId &&
                !c.IsDeleting, ct);
        if (db.Entry(gameBox).State == EntityState.Detached ||
            !competitionStillRunning ||
            !teamStillActive ||
            !challengeStillActive ||
            string.IsNullOrWhiteSpace(gameBox.ContainerInstanceId) ||
            !string.Equals(gameBox.ContainerInstanceId, expectedInstanceId, StringComparison.Ordinal) ||
            (gameBox.ExpiresAt is not null && gameBox.ExpiresAt <= switchNow))
        {
            ActivateContainerCleanupTask(candidateCleanupTask, patchedContainer);
            var restoredPreviousSuccess = await FindPreviousSuccessfulPatchAsync(submission, ct) is not null;
            await CompleteAndCommitAsync(
                AwdpPatchStatus.Rejected,
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                "Instance changed or expired during patch validation.",
                preserveSuccessfulState: restoredPreviousSuccess);
            return;
        }

        var oldInstance = ToContainerInstance(gameBox, gameBox.ContainerInstanceId);
        ApplyContainerMetadata(
            gameBox,
            patchedContainer,
            CreateOperationId(submission.Id, "patched-container"));
        gameBox.ExpiresAt ??= DateTime.UtcNow.AddHours(2);
        gameBox.LastInstanceActionAt = DateTime.UtcNow;
        if (oldInstance is not null &&
            !string.Equals(oldInstance.ContainerId, patchedContainer.ContainerId, StringComparison.Ordinal))
        {
            ActivateContainerCleanupTask(candidateCleanupTask, oldInstance);
        }
        else
        {
            CompleteContainerCleanupSafeguard(candidateCleanupTask);
        }

        await CompleteAndCommitAsync(
            AwdpPatchStatus.Verified,
            AwdpFixStatus.FixSuccess,
            AwdpServiceStatus.ServiceOk,
            checkResult.Detail);

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
        var env = await BuildPatchEnvironmentAsync(submission, ct);
        var timeout = TimeSpan.FromSeconds(config.FixTimeoutSeconds);
        var sandboxConfig = new ContainerConfig(
            Image: challenge.ContainerImage!,
            Command: BuildFixScriptCommand(),
            EnvironmentVariables: env,
            Labels: BuildLabels(submission.CompetitionId, submission.TeamId, submission.ChallengeId),
            NetworkName: ResolveUtilityNetwork(),
            Ttl: timeout,
            Entrypoint: ["/bin/sh", "-c"],
            OrchestrationJson: TrustedUtilityOrchestrationJson(),
            OperationId: CreateOperationId(submission.Id, "fix-script"));

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
            return (AwdpFixStatus.FixScriptError, "Sandbox execution failed.");
        }
    }

    private async Task<ContainerInstance> CreatePatchedContainerAsync(
        Challenge challenge,
        AwdpPatchSubmission submission,
        DateTime? expiresAt,
        CancellationToken ct)
    {
        var environment = await BuildRuntimeEnvironmentAsync(
            challenge,
            submission.CompetitionId,
            submission.TeamId,
            submission.ChallengeId,
            submission,
            ct);
        var patchedContainerConfig = new ContainerConfig(
            Image: challenge.ContainerImage!,
            EnvironmentVariables: environment,
            Labels: BuildLabels(submission.CompetitionId, submission.TeamId, submission.ChallengeId),
            PortMappings: BuildPortMappings(challenge),
            Ttl: expiresAt is { } deadline && deadline > DateTime.UtcNow
                ? deadline - DateTime.UtcNow
                : TimeSpan.FromHours(2),
            OrchestrationJson: challenge.OrchestrationJson,
            NetworkAliases: [BuildGameBoxAlias(submission.TeamId, submission.ChallengeId)],
            OperationId: CreateOperationId(submission.Id, "patched-container"));

        return await containerManager.CreateContainerAsync(patchedContainerConfig, ct);
    }

    private async Task<Dictionary<string, string>> BuildRuntimeEnvironmentAsync(
        Challenge challenge,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        AwdpPatchSubmission? patch,
        CancellationToken ct)
    {
        var environment = BuildChallengeEnvironment(challenge);
        if (patch is not null)
        {
            foreach (var (key, value) in await BuildPatchEnvironmentAsync(patch, ct))
                environment[key] = value;
        }

        var dynamicFlag = await db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(f =>
                f.CompetitionId == competitionId &&
                f.TeamId == teamId &&
                f.ChallengeId == challengeId,
                ct);

        if (dynamicFlag is not null)
        {
            var envName = string.IsNullOrWhiteSpace(dynamicFlag.EnvironmentVariable)
                ? string.IsNullOrWhiteSpace(challenge.FlagEnvironmentVariable)
                    ? "NOCTF_FLAG_UUID"
                    : challenge.FlagEnvironmentVariable.Trim()
                : dynamicFlag.EnvironmentVariable.Trim();
            environment[envName] = FormatFlag(challenge, dynamicFlag.FlagUuid);
        }

        return environment;
    }

    private async Task<CheckOutcome> RunCheckAsync(
        Challenge challenge,
        AwdpPatchSubmission submission,
        ContainerInstance targetContainer,
        CancellationToken ct)
    {
        if (challenge.CheckerConfig?.Image is null)
        {
            return new CheckOutcome(
                AwdpFixStatus.FixRuleViolation,
                AwdpServiceStatus.ServiceError,
                "AWDP check container is required before a FixScript can be verified.");
        }

        // Candidate safeguards are recovered after ten minutes. Keep the
        // checker deadline strictly below that window so a live validation
        // cannot have its candidate reclaimed by recovery.
        var timeout = TimeSpan.FromSeconds(Math.Clamp(challenge.CheckerConfig.TimeoutSeconds ?? 30, 1, 300));
        var deadline = DateTime.UtcNow.Add(timeout);
        CheckOutcome? lastOutcome = null;
        var attempt = 0;
        try
        {
            do
            {
                attempt++;
                var checkConfig = new ContainerConfig(
                    Image: challenge.CheckerConfig.Image,
                    Command: challenge.CheckerConfig.Command,
                    EnvironmentVariables: BuildCheckEnvironment(challenge, submission),
                    Labels: BuildLabels(submission.CompetitionId, submission.TeamId, submission.ChallengeId),
                    NetworkName: targetContainer.OrchestrationNamespace,
                    Ttl: timeout,
                    OperationId: CreateOperationId(submission.Id, $"check:{attempt}"));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp((deadline - DateTime.UtcNow).TotalSeconds + 5, 5, 15)));
                var result = await containerManager.RunContainerAsync(checkConfig, cts.Token);
                logger.LogDebug(
                    "AWDP check container for submission {SubmissionId}: exit={ExitCode}.",
                    submission.Id, result.ExitCode);

                lastOutcome = MapCheckExitCode(result.ExitCode, result.StdErr);
                if (lastOutcome.FixStatus != AwdpFixStatus.FixServiceError)
                    return lastOutcome;

                if (DateTime.UtcNow < deadline && IsRetryableCheckError(result))
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                else
                    return lastOutcome;
            } while (DateTime.UtcNow < deadline);

            return lastOutcome ?? new CheckOutcome(
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                "Check failed before producing a result.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("AWDP check container timed out for submission {SubmissionId}.", submission.Id);
            return new CheckOutcome(
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                "Check timed out; treating defense as service error.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AWDP check container failed for submission {SubmissionId}.", submission.Id);
            return new CheckOutcome(
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                "Check container failed.");
        }
    }

    private static ContainerInstance? ToContainerInstance(AwdGameBox gameBox, string? containerId)
        => string.IsNullOrWhiteSpace(containerId)
            ? null
            : new ContainerInstance(
                Guid.NewGuid(),
                gameBox.CompetitionId,
                gameBox.TeamId,
                gameBox.ChallengeId,
                gameBox.ProviderType,
                containerId,
                ReadPorts(gameBox.PortMappingsJson),
                "running",
                DateTime.UtcNow,
                PublicHost: gameBox.PublicHost,
                EntryUrl: gameBox.EntryUrl,
                OrchestrationNamespace: gameBox.OrchestrationNamespace);

    private static void ApplyContainerMetadata(
        AwdGameBox gameBox,
        ContainerInstance container,
        Guid operationId)
    {
        gameBox.ContainerInstanceId = container.ContainerId;
        gameBox.ProviderType = container.ProviderType;
        gameBox.PublicHost = container.PublicHost;
        gameBox.EntryUrl = container.EntryUrl;
        gameBox.OrchestrationNamespace = container.OrchestrationNamespace;
        gameBox.PortMappingsJson = JsonSerializer.Serialize(container.PortMappings, JsonOptions);
        gameBox.RuntimeKind = "container";
        gameBox.ComposeProjectName = null;
        gameBox.ComposeYaml = null;
        gameBox.InternalHost = container.InternalHost;
        gameBox.InternalPortMappingsJson = JsonSerializer.Serialize(
            container.InternalPortMappings ?? [],
            JsonOptions);
        gameBox.RuntimeOperationId = operationId;
        gameBox.CleanupOwner = null;
        gameBox.CleanupLockedUntil = null;
    }

    private async Task CompleteValidationAsync(
        AwdpPatchSubmission submission,
        AwdpTeamChallengeState state,
        AwdpPatchStatus patchStatus,
        AwdpFixStatus fixStatus,
        AwdpServiceStatus serviceStatus,
        string detail,
        CancellationToken ct,
        bool preserveSuccessfulState = false)
    {
        var now = DateTime.UtcNow;
        submission.Status = patchStatus;
        submission.FixStatus = fixStatus;
        submission.ValidatedAt = now;
        submission.ValidationDetail = detail;

        if (preserveSuccessfulState)
        {
            state.FixStatus = AwdpFixStatus.FixSuccess;
            state.ServiceStatus = AwdpServiceStatus.ServiceOk;
            state.LastValidationDetail = $"Latest FixScript rejected; previous verified FixScript restored. {detail}";
        }
        else
        {
            state.FixStatus = fixStatus;
            state.ServiceStatus = serviceStatus;
            state.LastValidationDetail = detail;
        }
        state.FixSucceededAt = fixStatus == AwdpFixStatus.FixSuccess ? now : state.FixSucceededAt;
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

    private async Task<Dictionary<string, string>> BuildPatchEnvironmentAsync(
        AwdpPatchSubmission submission,
        CancellationToken ct)
    {
        var patchUrl = submission.PatchArchiveUrl;
        if (!string.IsNullOrWhiteSpace(patchUrl) &&
            !patchUrl.StartsWith("/", StringComparison.Ordinal) &&
            !Uri.TryCreate(patchUrl, UriKind.Absolute, out _))
        {
            patchUrl = storageProvider is ITemporaryUrlStorageProvider temporaryUrls
                ? await temporaryUrls.GetUrlAsync(patchUrl, TimeSpan.FromHours(3), ct)
                : await storageProvider.GetUrlAsync(patchUrl, ct);
        }

        return new Dictionary<string, string>
        {
            ["PATCH_URL"] = ResolveDownloadUrl(patchUrl),
            ["PATCH_FILE_NAME"] = submission.FileName,
            ["FIX_ENTRY"] = submission.FixEntry
        };
    }

    private static Dictionary<string, string> BuildProbeEnvironment(Challenge challenge, AwdpPatchSubmission submission)
        => new()
        {
            ["TARGET_HOST"] = BuildGameBoxAlias(submission.TeamId, submission.ChallengeId),
            ["TARGET_PORT"] = ResolveTargetPort(challenge).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TEAM_ID"] = submission.TeamId.ToString()
        };

    private static string BuildGameBoxAlias(Guid teamId, Guid challengeId)
        => $"gamebox-{ShortId(teamId)}-{ShortId(challengeId)}";

    private static string ShortId(Guid id)
        => id.ToString("N")[..8];

    private Dictionary<string, string> BuildCheckEnvironment(Challenge challenge, AwdpPatchSubmission submission)
        => BuildProbeEnvironment(challenge, submission);

    private static CheckOutcome MapCheckExitCode(int exitCode, string? stderr)
    {
        var suffix = string.IsNullOrWhiteSpace(stderr)
            ? string.Empty
            : $" stderr={stderr.Trim()}";

        return exitCode switch
        {
            0 => new CheckOutcome(
                AwdpFixStatus.FixSuccess,
                AwdpServiceStatus.ServiceOk,
                $"Check exited 0; FixScript verified for future round defense scoring.{suffix}"),
            1 => new CheckOutcome(
                AwdpFixStatus.FixFailed,
                AwdpServiceStatus.ServiceOk,
                $"Check exited 1; EXP exploit succeeded and the vulnerability still exists.{suffix}"),
            2 => new CheckOutcome(
                AwdpFixStatus.FixRuleViolation,
                AwdpServiceStatus.ServiceError,
                $"Check exited 2; bad or rule-violating patch detected.{suffix}"),
            3 => new CheckOutcome(
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                $"Check exited 3; interaction error or service behavior failure.{suffix}"),
            _ => new CheckOutcome(
                AwdpFixStatus.FixServiceError,
                AwdpServiceStatus.ServiceError,
                $"Check exited with unsupported code {exitCode}; treating defense as service error.{suffix}")
        };
    }

    private static bool IsRetryableCheckError(ContainerRunResult result)
    {
        if (result.ExitCode != 3)
            return false;

        var output = $"{result.StdErr}\n{result.StdOut}";
        return output.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("timed out", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildLabels(
        Guid competitionId,
        Guid teamId,
        Guid challengeId)
        => new()
        {
            ["competitionId"] = competitionId.ToString(),
            ["teamId"] = teamId.ToString(),
            ["challengeId"] = challengeId.ToString()
        };

    private async Task<AwdpPatchSubmission?> FindPreviousSuccessfulPatchAsync(
        AwdpPatchSubmission rejectedSubmission,
        CancellationToken ct)
        => await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == rejectedSubmission.CompetitionId &&
                s.TeamId == rejectedSubmission.TeamId &&
                s.ChallengeId == rejectedSubmission.ChallengeId &&
                s.Id != rejectedSubmission.Id &&
                s.Status == AwdpPatchStatus.Verified &&
                s.FixStatus == AwdpFixStatus.FixSuccess)
            .OrderByDescending(s => s.ValidatedAt ?? s.SubmittedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<BackgroundTaskItem> GetOrCreateContainerCleanupSafeguardAsync(
        Guid competitionId,
        Guid submissionId,
        ContainerInstance container,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var marker = $"awdp-candidate:{submissionId:N}";
        var existing = await db.BackgroundTasks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(task =>
                task.CompetitionId == competitionId &&
                task.Type == AwdpBackgroundTaskTypes.ContainerCleanup &&
                task.LastError == marker &&
                task.Status != BackgroundTaskStatus.Succeeded &&
                task.Status != BackgroundTaskStatus.Failed &&
                task.Status != BackgroundTaskStatus.Cancelled, ct);
        if (existing is not null)
        {
            existing.Status = BackgroundTaskStatus.Running;
            existing.PayloadJson = JsonSerializer.Serialize(new AwdpContainerCleanupPayload(container), JsonOptions);
            existing.LockOwner = $"awdp-validation:{submissionId:N}";
            existing.LockedUntil = now.AddMinutes(10);
            existing.UpdatedAt = now;
            return existing;
        }

        var task = new BackgroundTaskItem
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            Type = AwdpBackgroundTaskTypes.ContainerCleanup,
            Status = BackgroundTaskStatus.Running,
            PayloadJson = JsonSerializer.Serialize(new AwdpContainerCleanupPayload(container), JsonOptions),
            AttemptCount = 0,
            MaxAttempts = 5,
            LockOwner = $"awdp-validation:{submissionId:N}",
            LockedUntil = now.AddMinutes(10),
            LastError = marker,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.BackgroundTasks.Add(task);
        return task;
    }

    private static void ActivateContainerCleanupTask(
        BackgroundTaskItem task,
        ContainerInstance container)
    {
        task.PayloadJson = JsonSerializer.Serialize(new AwdpContainerCleanupPayload(container), JsonOptions);
        task.Status = BackgroundTaskStatus.Pending;
        task.LockOwner = null;
        task.LockedUntil = null;
        task.LastError = null;
        task.UpdatedAt = DateTime.UtcNow;
    }

    private static void CompleteContainerCleanupSafeguard(BackgroundTaskItem task)
    {
        task.Status = BackgroundTaskStatus.Succeeded;
        task.LockOwner = null;
        task.LockedUntil = null;
        task.LastError = null;
        task.UpdatedAt = DateTime.UtcNow;
    }

    private async Task DestroyCandidateBestEffortAsync(ContainerInstance container)
    {
        try
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await containerManager.DestroyContainerAsync(container, cleanupCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to destroy untracked AWDP candidate container {ContainerId} after cleanup-saga persistence failed.",
                container.ContainerId);
        }
    }

    private void AddBackgroundTask<TPayload>(Guid competitionId, string type, TPayload payload)
    {
        var now = DateTime.UtcNow;
        db.BackgroundTasks.Add(new BackgroundTaskItem
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            Type = type,
            Status = BackgroundTaskStatus.Pending,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            AttemptCount = 0,
            MaxAttempts = 5,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static string TrustedUtilityOrchestrationJson()
    {
        var spec = new OrchestrationSpec
        {
            Kubernetes = new KubernetesOrchestrationSpec
            {
                NetworkMode = OrchestrationNetworkMode.Open
            }
        };

        return OrchestrationSpecSerializer.Write(spec);
    }

    private string ResolveDownloadUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        if (!url.StartsWith("/", StringComparison.Ordinal) &&
            Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        var publicBaseUrl = FirstNonBlank(
            configuration["StorageProvider:PublicBaseUrl"],
            configuration["App:PublicBaseUrl"],
            configuration["NoCTF:PublicBaseUrl"],
            Environment.GetEnvironmentVariable("NOCTF_STORAGE_PUBLIC_BASE_URL"));
        if (publicBaseUrl is null && IsRunningInContainer())
            publicBaseUrl = "http://backend:8080";

        if (publicBaseUrl is null)
        {
            logger.LogWarning(
                "AWDP FixScript archive URL '{Url}' is relative and no StorageProvider:PublicBaseUrl is configured.",
                url);
            return url;
        }

        return $"{publicBaseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
    }

    private string? ResolveUtilityNetwork()
    {
        if (!configuration.GetValue("Awdp:FixSandboxAllowUtilityNetwork", false))
            return null;

        return FirstNonBlank(
            configuration["Runner:UtilityNetwork"],
            configuration["Docker:UtilityNetwork"]);
    }

    private static bool IsRunningInContainer()
        => string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);

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
    {
        var exposedPort = ResolveConfiguredPort(challenge);
        return exposedPort is > 0
            ? new Dictionary<int, int> { [exposedPort.Value] = 0 }
            : null;
    }

    private static int ResolveTargetPort(Challenge challenge)
        => ResolveConfiguredPort(challenge) ?? 80;

    private static int? ResolveConfiguredPort(Challenge challenge)
    {
        var spec = OrchestrationSpecSerializer.Read(challenge.OrchestrationJson);
        return spec.ExposedPort is > 0 ? spec.ExposedPort : challenge.ExposedPort;
    }

    private static Dictionary<string, string> BuildChallengeEnvironment(Challenge challenge)
    {
        var spec = OrchestrationSpecSerializer.Read(challenge.OrchestrationJson);
        return spec.Environment.Count > 0
            ? new Dictionary<string, string>(spec.Environment, StringComparer.Ordinal)
            : [];
    }

    private static string FormatFlag(Challenge challenge, string content)
    {
        var prefix = string.IsNullOrWhiteSpace(challenge.FlagPrefix) ? "flag" : challenge.FlagPrefix.Trim();
        if (prefix.Contains("{0}", StringComparison.Ordinal))
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, prefix, content);

        if (prefix.Contains("{}", StringComparison.Ordinal))
            return prefix.Replace("{}", $"{{{content}}}", StringComparison.Ordinal);

        var braceIndex = prefix.IndexOf('{', StringComparison.Ordinal);
        if (braceIndex >= 0)
            prefix = prefix[..braceIndex].Trim();

        return $"{prefix}{{{content}}}";
    }

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
           download_url="$PATCH_URL"
           case "$download_url" in
             http://*|https://*) ;;
             /*) download_url="${NOCTF_PATCH_BASE_URL:-http://backend:8080}$download_url" ;;
           esac
           rm -rf "$work"
           mkdir -p "$work"
           if command -v curl >/dev/null 2>&1; then
             curl -fsSL "$download_url" -o "$archive"
           elif command -v wget >/dev/null 2>&1; then
             wget -q "$download_url" -O "$archive"
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

    private static Guid CreateOperationId(Guid submissionId, string stage)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"awdp:{submissionId:N}:{stage}"));
        return new Guid(digest.AsSpan(0, 16));
    }

    private static bool IsTerminal(AwdpFixStatus status)
        => status is AwdpFixStatus.FixSuccess
            or AwdpFixStatus.FixFailed
            or AwdpFixStatus.FixServiceError
            or AwdpFixStatus.FixScriptError
            or AwdpFixStatus.FixTimeout
            or AwdpFixStatus.AuditFailed
            or AwdpFixStatus.FixRuleViolation
            or AwdpFixStatus.DefenseAttemptsExhausted;

    internal static bool IsContainerOperationCoolingDown(DateTime? lastInstanceActionAt, DateTime now)
        => lastInstanceActionAt?.Add(ContainerOperationCooldown) > now;

    private sealed record CheckOutcome(
        AwdpFixStatus FixStatus,
        AwdpServiceStatus ServiceStatus,
        string Detail);
}

internal sealed class AwdpPatchStateLock : IAsyncDisposable
{
    private readonly IDbContextTransaction? _transaction;
    private bool _committed;

    private AwdpPatchStateLock(IDbContextTransaction? transaction)
    {
        _transaction = transaction;
    }

    public static async Task<AwdpPatchStateLock> AcquireAsync(
        ApplicationDbContext db,
        Guid teamId,
        Guid challengeId,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return new AwdpPatchStateLock(null);

        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdvisoryLockKey(teamId, challengeId)})",
                ct);
        }

        return new AwdpPatchStateLock(transaction);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null || _committed)
            return;

        await _transaction.CommitAsync(ct);
        _committed = true;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_transaction is null || _committed)
            return;

        await _transaction.RollbackAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
    }

    private static long AdvisoryLockKey(Guid teamId, Guid challengeId)
    {
        var teamPart = BitConverter.ToInt32(teamId.ToByteArray(), 0);
        var challengePart = BitConverter.ToInt32(challengeId.ToByteArray(), 0);
        return ((long)teamPart << 32) ^ (uint)challengePart;
    }
}
