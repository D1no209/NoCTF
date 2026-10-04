using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class SourcePackageTests
{
    [Test]
    [Timeout(60_000)]
    public async Task Package_includes_actual_build_sources_and_excludes_untracked_files(CancellationToken ct)
    {
        await using var fixture = await SourceFixture.CreateAsync(ct);
        var translated = "{\"message\":\"translation used by this build\"}";
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository, "catalog.json"), translated, ct);
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository, ".env"), "LOCAL_ONLY=value", ct);
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository, "untracked.txt"), "local note", ct);

        var result = await fixture.PackageAsync("source.tar.gz", ct);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        var entries = ReadArchive(Path.Combine(fixture.Root, "source.tar.gz"));
        await Assert.That(entries["noctf-source/catalog.json"]).IsEqualTo(translated);
        await Assert.That(entries.Keys).Contains("noctf-source/LICENSE");
        await Assert.That(entries.Keys).DoesNotContain("noctf-source/.env");
        await Assert.That(entries.Keys).DoesNotContain("noctf-source/untracked.txt");
        using var manifest = JsonDocument.Parse(entries["noctf-source/.source-manifest.json"]);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(translated)));
        await Assert.That(manifest.RootElement.GetProperty("files").GetProperty("catalog.json").GetString())
            .IsEqualTo(expectedHash);
        var archiveHash = Convert.ToHexStringLower(SHA256.HashData(
            await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "source.tar.gz"), ct)));
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "source.tar.gz.sha256"), ct))
            .IsEqualTo($"{archiveHash}  source.tar.gz\n");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Package_is_reproducible_for_identical_build_sources(CancellationToken ct)
    {
        await using var fixture = await SourceFixture.CreateAsync(ct);
        var first = await fixture.PackageAsync("first.tar.gz", ct);
        var second = await fixture.PackageAsync("second.tar.gz", ct);
        await Assert.That(first.ExitCode).IsEqualTo(0);
        await Assert.That(second.ExitCode).IsEqualTo(0);
        var firstHash = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "first.tar.gz"), ct));
        var secondHash = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "second.tar.gz"), ct));
        await Assert.That(Convert.ToHexString(firstHash)).IsEqualTo(Convert.ToHexString(secondHash));
    }

    [Test]
    [Timeout(60_000)]
    public async Task Backend_only_package_identifies_the_preserved_frontend_image(CancellationToken ct)
    {
        await using var fixture = await SourceFixture.CreateAsync(ct);
        var baseImage = $"registry.example.test/noctf/base@sha256:{new string('a', 64)}";

        var result = await fixture.PackageAsync("backend.tar.gz", ct, baseImage);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        using var manifest = JsonDocument.Parse(ReadArchive(Path.Combine(fixture.Root, "backend.tar.gz"))
            ["noctf-source/.source-manifest.json"]);
        await Assert.That(manifest.RootElement.GetProperty("frontendBaseImage").GetString()).IsEqualTo(baseImage);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Package_rejects_a_source_distribution_without_a_committed_license(CancellationToken ct)
    {
        await using var fixture = await SourceFixture.CreateAsync(ct);
        await fixture.RunAsync("git", ["rm", "LICENSE"], ct);

        var result = await fixture.PackageAsync("missing-license.tar.gz", ct);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Error).Contains("Commit LICENSE");
        await Assert.That(File.Exists(Path.Combine(fixture.Root, "missing-license.tar.gz"))).IsFalse();
    }

    private static Dictionary<string, string> ReadArchive(string path)
    {
        using var input = File.OpenRead(path);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var archive = new TarReader(gzip);
        var result = new Dictionary<string, string>();
        while (archive.GetNextEntry(copyData: true) is { } entry)
        {
            using var reader = new StreamReader(entry.DataStream ?? Stream.Null);
            result.Add(entry.Name, reader.ReadToEnd());
        }
        return result;
    }

    private sealed class SourceFixture(string root, string repository) : IAsyncDisposable
    {
        public string Root { get; } = root;
        public string Repository { get; } = repository;

        public static async Task<SourceFixture> CreateAsync(CancellationToken ct)
        {
            var root = Path.Combine(Path.GetTempPath(), $"noctf-source-package-{Guid.NewGuid():N}");
            var repository = Path.Combine(root, "repository");
            Directory.CreateDirectory(repository);
            var fixture = new SourceFixture(root, repository);
            try
            {
                await fixture.RunAsync("git", ["init"], ct);
                await File.WriteAllTextAsync(Path.Combine(repository, "LICENSE"), "license fixture\n", ct);
                await File.WriteAllTextAsync(Path.Combine(repository, "catalog.json"), "{}", ct);
                await fixture.RunAsync("git", ["add", "LICENSE", "catalog.json"], ct);
                await fixture.RunAsync("git", ["-c", "user.name=Source package test", "-c",
                    "user.email=source-package@example.test", "commit", "-m", "source fixture"], ct);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public Task<ProcessResult> PackageAsync(string name, CancellationToken ct, string? frontendBaseImage = null)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                directory = directory.Parent;
            var script = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(),
                "backend", "scripts", "Package-Source.py");
            List<string> arguments = [script, "--root", Repository, "--output", Path.Combine(Root, name)];
            if (frontendBaseImage is not null) arguments.AddRange(["--frontend-base-image", frontendBaseImage]);
            return RunAsync(OperatingSystem.IsWindows() ? "python" : "python3", arguments.ToArray(), ct);
        }

        public async Task<ProcessResult> RunAsync(string executable, string[] arguments, CancellationToken ct)
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = Repository,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start source test.");
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var result = new ProcessResult(process.ExitCode, await output, await error);
            if (executable == "git" && result.ExitCode != 0)
                throw new InvalidOperationException(result.Error);
            return result;
        }

        public ValueTask DisposeAsync()
        {
            var resolved = Path.GetFullPath(Root);
            var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(resolved), expectedParent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("noctf-source-package-", StringComparison.Ordinal))
                throw new InvalidOperationException("Source fixture cleanup left the temporary directory.");
            if (Directory.Exists(resolved))
            {
                foreach (var file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                Directory.Delete(resolved, recursive: true);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
