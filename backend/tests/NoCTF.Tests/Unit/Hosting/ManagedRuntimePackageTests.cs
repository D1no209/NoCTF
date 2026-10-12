using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class ManagedRuntimePackageTests
{
    [Test]
    [Arguments(false, false, 0)]
    [Arguments(true, false, 1)]
    [Arguments(false, true, 1)]
    public async Task Package_integrity_and_source_identity_are_verified_before_restore(bool corruptPackage, bool wrongSource, int expectedExit)
    {
        var backend = FindBackend();
        using var locked = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(backend, "runtime", "runtime-lock.json")));
        var source = locked.RootElement.GetProperty("source");
        var directory = Path.Combine(Path.GetTempPath(), "noctf-managed-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string version = "10.0.12-satori.test";
            var packages = new List<object>();
            foreach (var kind in new[] { "Runtime", "Host" })
            {
                var id = $"Microsoft.NETCore.App.{kind}.win-x64";
                var name = id + ".nupkg";
                var path = Path.Combine(directory, name);
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    await using var writer = new StreamWriter(archive.CreateEntry(id + ".nuspec").Open());
                    await writer.WriteAsync($"<package><metadata><id>{id}</id><version>{version}</version></metadata></package>");
                }
                var sha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
                packages.Add(new { id, version, file = name, sha256 = sha });
            }
            var manifest = new
            {
                rid = "win-x64", productVersion = "10.0.12", packageVersion = version, packages,
                source = new
                {
                    commit = wrongSource ? new string('0', 40) : source.GetProperty("commit").GetString(),
                    upstreamCommit = source.GetProperty("upstreamCommit").GetString(), upstreamIsAncestor = true
                }
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
            if (corruptPackage)
                await File.AppendAllTextAsync(Path.Combine(directory, "Microsoft.NETCore.App.Runtime.win-x64.nupkg"), "changed");
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                WorkingDirectory = backend
            };
            foreach (var argument in new[] { "runtime/runtime.py", "verify", "--runtime-dir", directory, "--rid", "win-x64" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            await Assert.That(process.ExitCode).IsEqualTo(expectedExit);
            if (expectedExit == 0) await Assert.That(await stdout).Contains("verified");
        }
        finally
        {
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a path outside the test temporary directory.");
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindBackend()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("NoCTF backend was not found.");
    }
}
