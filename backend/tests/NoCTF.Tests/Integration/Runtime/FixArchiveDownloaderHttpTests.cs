using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Runner.Challenges.Capabilities;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class FixArchiveDownloaderHttpTests
{
    public enum Transfer { Valid, WrongHash, WrongLength, Forbidden, Interrupted, SlowHeaders, SlowBody }

    [Test, Timeout(180_000)]
    [Arguments(Transfer.Valid, FixArchiveDownloadOutcome.Downloaded)]
    [Arguments(Transfer.WrongHash, FixArchiveDownloadOutcome.IntegrityMismatch)]
    [Arguments(Transfer.WrongLength, FixArchiveDownloadOutcome.IntegrityMismatch)]
    [Arguments(Transfer.Forbidden, FixArchiveDownloadOutcome.HttpStatusRejected)]
    [Arguments(Transfer.Interrupted, FixArchiveDownloadOutcome.TransferInterrupted)]
    [Arguments(Transfer.SlowHeaders, FixArchiveDownloadOutcome.TimedOut)]
    [Arguments(Transfer.SlowBody, FixArchiveDownloadOutcome.TimedOut)]
    public async Task Actual_http_transfer_checks_credentials_integrity_deadlines_and_partial_file_cleanup(Transfer transfer, FixArchiveDownloadOutcome expected, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes("immutable-archive-payload");
        var request = new TaskCompletionSource<(string Method, string? Authorization)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0"); builder.Services.AddHttpClient(FixArchiveDownloader.ClientName);
        await using var server = builder.Build();
        server.Run(async context =>
        {
            request.TrySetResult((context.Request.Method, context.Request.Headers.Authorization));
            if (transfer == Transfer.Forbidden) { context.Response.StatusCode = 403; return; }
            if (transfer == Transfer.SlowHeaders) { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); return; }
            context.Response.ContentLength = bytes.Length;
            await context.Response.StartAsync(context.RequestAborted);
            if (transfer is Transfer.Interrupted or Transfer.SlowBody)
            {
                await context.Response.Body.WriteAsync(bytes.AsMemory(0, 4), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                if (transfer == Transfer.SlowBody) await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                else { await Task.Delay(100, context.RequestAborted); context.Abort(); }
                return;
            }
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        });
        await server.StartAsync(ct);
        var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var hash = SHA256.HashData(transfer == Transfer.WrongHash ? Encoding.UTF8.GetBytes("different-payload") : bytes);
        var archive = new FixArchive(new Uri(address + "/archive"), "archive-read-credential", "fix.tar.gz",
            transfer == Transfer.WrongLength ? bytes.Length + 1 : bytes.Length, hash);
        var directory = Path.Combine(AppContext.BaseDirectory, "archive-download-tests", Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(directory, "archive.tar.gz");
        try
        {
            var outcome = await new FixArchiveDownloader(server.Services.GetRequiredService<IHttpClientFactory>()).DownloadAsync(archive, destination, ct);
            await Assert.That(outcome).IsEqualTo(expected);
            var observed = await request.Task.WaitAsync(ct);
            await Assert.That(observed.Method).IsEqualTo("GET");
            await Assert.That(observed.Authorization).IsEqualTo("Bearer archive-read-credential");
            if (expected == FixArchiveDownloadOutcome.Downloaded)
                await Assert.That(await File.ReadAllBytesAsync(destination, ct)).IsEquivalentTo(bytes);
            else await Assert.That(File.Exists(destination)).IsFalse();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task Archive_diagnostics_keep_credentials_and_private_metadata_redacted()
    {
        var archive = new FixArchive(new Uri("https://private.invalid/protected/archive"), "private-credential", "private-file-name", 16, new byte[32]);
        var text = archive.ToString();
        await Assert.That(text.Contains("private-credential", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains("private-file-name", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains("private.invalid", StringComparison.Ordinal)).IsFalse();
    }
}
