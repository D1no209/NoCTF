using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.PatchUploads;
using NoCTF.Application.Submissions.Processing;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;

namespace NoCTF.Runner.Messages;

public sealed class AwdpFixVerificationHandler(
    NoCtfDbContext db,
    IFixArchiveReader archives,
    IObjectStorage objects,
    FixArchivePreparer preparer,
    IContainerSandboxLifecycle sandbox,
    ITransactionalMessageOutbox outbox)
{
    public async Task Handle(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken)
    {
        var submission = await db.Submissions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == message.SubmissionId, cancellationToken);
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == message.RuntimeInstanceId, cancellationToken);
        if (submission is null || runtime is null
            || submission.ProcessingVersion != message.ProcessingVersion
            || runtime.Generation != message.Generation
            || string.IsNullOrWhiteSpace(runtime.ProviderReceiptJson))
            return;

        var occurredAt = DateTimeOffset.UtcNow;
        var exitCode = 3;
        var timedOut = false;
        var workDirectory = Path.Combine(Path.GetTempPath(), "noctf-awdp", message.SubmissionId.ToString("N"));
        var tarPath = Path.Combine(workDirectory, "fix.tar");
        try
        {
            var archive = await archives.FindAsync(message.SubmissionId, cancellationToken);
            var upload = await db.PatchUploads.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == message.PatchUploadId
                    && item.SubmissionId == message.SubmissionId, cancellationToken);
            var stored = archive is null || upload is null
                ? null
                : await objects.InspectAsync(upload.ObjectKey, cancellationToken);
            if (archive is null || upload is null || stored is null
                || stored.Length != upload.ByteLength
                || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(stored.Sha256), upload.Sha256))
            {
                exitCode = 3;
            }
            else
            {
                await using var source = await objects.OpenReadAsync(upload.ObjectKey, cancellationToken);
                await preparer.PrepareTarAsync(
                    source, upload.OriginalFileName, workDirectory, tarPath, cancellationToken);
                var receipt = JsonSerializer.Deserialize<ContainerReceipt>(runtime.ProviderReceiptJson)
                    ?? throw new InvalidDataException("Runtime provider receipt is invalid.");
                await using (var tar = File.OpenRead(tarPath))
                    await sandbox.CopyArchiveAsync(receipt, tar, cancellationToken);

                var challenge = await db.CompetitionChallenges.AsNoTracking()
                    .SingleAsync(item => item.Id == submission.CompetitionChallengeId, cancellationToken);
                var configuration = AwdpConfigurationParser.ParseChallenge(challenge.ConfigurationJson);
                var patchCommand = configuration.PatchCommand is { Count: > 0 }
                    ? configuration.PatchCommand
                    : ["/bin/sh", $"/noctf/fix/{configuration.PatchEntrypoint}"];
                var patchResult = await sandbox.ExecAsync(
                    receipt, patchCommand, TimeSpan.FromSeconds(configuration.PatchTimeoutSeconds), cancellationToken);
                timedOut = patchResult.TimedOut;
                exitCode = timedOut ? 3 : patchResult.ExitCode;

                if (!timedOut && exitCode == 0)
                {
                    var checker = configuration.Checker;
                    if (checker is null || checker.Command is not { Count: > 0 })
                        exitCode = 3;
                    else
                    {
                        var checkerResult = await sandbox.ExecAsync(
                            receipt, checker.Command, TimeSpan.FromSeconds(checker.TimeoutSeconds), cancellationToken);
                        timedOut = checkerResult.TimedOut;
                        exitCode = timedOut ? 3 : checkerResult.ExitCode;
                    }
                }
            }
        }
        catch (InvalidDataException)
        {
            exitCode = 2;
        }
        catch (FileNotFoundException)
        {
            exitCode = 3;
        }
        finally
        {
            if (Directory.Exists(workDirectory))
                Directory.Delete(workDirectory, recursive: true);
        }

        var body = Encoding.UTF8.GetBytes($"{exitCode}:{timedOut.ToString().ToLowerInvariant()}");
        await outbox.PublishAsync(new AwdpFixResult(
            message.SubmissionId,
            message.ProcessingVersion,
            exitCode,
            timedOut,
            SHA256.HashData(body),
            occurredAt));
        await outbox.FlushOutgoingMessagesAsync();
    }
}
