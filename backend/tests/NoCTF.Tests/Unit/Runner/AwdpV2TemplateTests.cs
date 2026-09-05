using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Runner.Composition;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpV2TemplateTests
{
    [Test]
    public async Task V2Template_IsIndependentCompleteAndEnablesFixInputOnSchemaFour()
    {
        var root = TemplateRoot();
        var expected = new[]
        {
            "README.md",
            "awdp-challenge-template.md",
            "starter-kits/awdp/.dockerignore",
            "starter-kits/awdp/NOCTF-DELIVERY.md",
            "starter-kits/awdp/README.md",
            "starter-kits/awdp/checker/Dockerfile",
            "starter-kits/awdp/checker/check.sh",
            "starter-kits/awdp/checker/exploit.sh",
            "starter-kits/awdp/checker/replay-and-diff.sh",
            "starter-kits/awdp/target/Dockerfile",
            "starter-kits/awdp/target/server.py",
            "starter-kits/awdp/platform/CONFIGURATION.md",
            "starter-kits/awdp/scripts/build-fix-packages.sh",
            "starter-kits/awdp/tests/smoke.sh"
        };
        foreach (var relative in expected)
            await Assert.That(File.Exists(Path.Combine(root, relative))).IsTrue();

        var configurationDocument = await File.ReadAllTextAsync(
            Path.Combine(root, "starter-kits", "awdp", "platform", "CONFIGURATION.md"));
        var json = Between(configurationDocument, "```json", "```");
        var parsed = AwdpConfigurationParser.ParseChallenge(json);
        var errors = new GameModeChallengeConfigurationCatalog().Validate(GameMode.Awdp, json);

        await Assert.That(parsed.SchemaVersion).IsEqualTo(4);
        await Assert.That(parsed.CheckerFixInput).IsTrue();
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task V2FixSamples_AreAcceptedByTheProductionArchivePreparer()
    {
        var fixes = Path.Combine(TemplateRoot(), "starter-kits", "awdp", "examples", "fixes");
        var preparer = new FixArchivePreparer(Options.Create(new FixVerificationOptions()));
        foreach (var source in Directory.EnumerateDirectories(fixes).Order(StringComparer.Ordinal))
        {
            var temporary = Path.Combine(
                Path.GetTempPath(),
                "noctf-v2-template-test",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporary);
                await using var archive = BuildTarGzip(source);
                var canonical = Path.Combine(temporary, "canonical.tar");

                await preparer.PrepareTarAsync(
                    archive,
                    "sample.tar.gz",
                    "fix.sh",
                    Path.Combine(temporary, "work"),
                    canonical,
                    CancellationToken.None);

                await Assert.That(new FileInfo(canonical).Length).IsGreaterThan(0);
                using var reader = new TarReader(File.OpenRead(canonical));
                var names = new List<string>();
                while (reader.GetNextEntry() is { } entry)
                    names.Add(entry.Name);
                await Assert.That(names).Contains("noctf/fix/fix.sh");
            }
            finally
            {
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, recursive: true);
            }
        }
    }

    [Test]
    public async Task V2Checker_DocumentsReplayIsolationAndAllTrustedOutcomesWithoutSecrets()
    {
        var root = TemplateRoot();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        var content = string.Join('\n', await Task.WhenAll(
            files.Select(file => File.ReadAllTextAsync(file))));

        await Assert.That(content).Contains("/noctf/fix");
        await Assert.That(content).Contains("replay-and-diff.sh");
        await Assert.That(content).Contains("DefenseSucceeded");
        await Assert.That(content).Contains("ExploitSucceeded");
        await Assert.That(content).Contains("ServiceAbnormal");
        await Assert.That(content).DoesNotContain("qaq-love.cn");
        await Assert.That(content).DoesNotContain("flag{");
        await Assert.That(content).DoesNotContain("eyJhbGciOi");
    }

    private static MemoryStream BuildTarGzip(string source)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.NoCompression, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                var name = Path.GetRelativePath(source, file).Replace('\\', '/');
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(File.ReadAllBytes(file))
                });
            }
        }
        output.Position = 0;
        return output;
    }

    private static string Between(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        if (startIndex < 0)
            throw new InvalidDataException($"Missing marker '{start}'.");
        startIndex += start.Length;
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
            throw new InvalidDataException($"Missing marker '{end}'.");
        return value[startIndex..endIndex].Trim();
    }

    private static string TemplateRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx")))
            {
                return Path.Combine(
                    directory.Parent!.FullName,
                    "docs",
                    "challenge-authoring-templates-v2");
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("NoCTF repository root was not found.");
    }
}
