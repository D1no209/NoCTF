using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
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
            "starter-kits/awdp/private/README.md",
            "starter-kits/awdp/private/DEPLOYMENT.md",
            "starter-kits/awdp/private/HINTS.md",
            "starter-kits/awdp/private/CHECKER-FIX.md",
            "starter-kits/awdp/private/checker/Dockerfile",
            "starter-kits/awdp/private/checker/check.sh",
            "starter-kits/awdp/private/checker/exploit.sh",
            "starter-kits/awdp/private/checker/replay-and-diff.sh",
            "starter-kits/awdp/private/checker/inspect-file.py",
            "starter-kits/awdp/private/target/Dockerfile",
            "starter-kits/awdp/private/target/server.py",
            "starter-kits/awdp/attachment/README.md",
            "starter-kits/awdp/attachment/public.txt",
            "starter-kits/awdp/attachment/patch-template/README.md",
            "starter-kits/awdp/attachment/patch-template/fix.sh",
            "starter-kits/awdp/platform/CONFIGURATION.md",
            "starter-kits/awdp/platform/FIELDS.md",
            "starter-kits/awdp/statement.md",
            "starter-kits/awdp/scripts/build-fix-packages.sh",
            "starter-kits/awdp/scripts/build-attachments.sh",
            "starter-kits/awdp/tests/packages.sh",
            "starter-kits/awdp/tests/input-contract.py",
            "starter-kits/awdp/tests/smoke.sh"
        };
        foreach (var relative in expected)
            await Assert.That(File.Exists(Path.Combine(root, relative))).IsTrue();

        var json = await File.ReadAllTextAsync(DefinitionFixture());
        var parsed = AwdpConfigurationParser.ParseChallenge(json);
        var errors = new GameModeChallengeConfigurationCatalog().Validate(GameMode.Awdp, json);

        await Assert.That(parsed.SchemaVersion).IsEqualTo(4);
        await Assert.That(parsed.CheckerFixInput).IsTrue();
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task V2FixSamples_AreAcceptedByTheProductionArchivePreparer()
    {
        var fixes = Path.Combine(TemplateRoot(), "starter-kits", "awdp", "private", "examples", "fixes");
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
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".md" or ".sh" or ".py" or ".txt"
                || Path.GetFileName(file) == "Dockerfile")
            .ToArray();
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

    [Test]
    public async Task V2Documentation_AllGuidesHaveChineseHeadings()
    {
        var documents = Directory.EnumerateFiles(TemplateRoot(), "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();
        await Assert.That(documents.Length).IsGreaterThanOrEqualTo(8);
        foreach (var file in documents)
        {
            var lines = await File.ReadAllLinesAsync(file);
            var headings = lines.Where(line => line.StartsWith('#')).ToArray();
            await Assert.That(headings.Length).IsGreaterThan(0);
            foreach (var heading in headings)
                await Assert.That(heading.Any(character => character is >= '\u4e00' and <= '\u9fff')).IsTrue();
        }
    }

    [Test]
    public async Task V2PublicPatchTemplate_IsIndependentAndAcceptedByTheArchivePreparer()
    {
        var kit = Path.Combine(TemplateRoot(), "starter-kits", "awdp");
        var source = Path.Combine(kit, "attachment", "patch-template");
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(source, path)).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(files.Length).IsEqualTo(2);
        await Assert.That(files).Contains("README.md");
        await Assert.That(files).Contains("fix.sh");
        var entrypoint = await File.ReadAllTextAsync(Path.Combine(source, "fix.sh"));
        await Assert.That(entrypoint).Contains("exit 1");
        await Assert.That(entrypoint).DoesNotContain("policy.txt");
        await Assert.That(File.Exists(Path.Combine(kit, "checker", "exploit.sh"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(kit, "target", "server.py"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(kit, "examples", "fixes", "defense-succeeded", "fix.sh"))).IsFalse();

        var temporary = Directory.CreateTempSubdirectory("noctf-v2-public-patch-");
        try
        {
            var preparer = new FixArchivePreparer(Options.Create(new FixVerificationOptions()));
            await using var archive = BuildTarGzip(source);
            var canonical = Path.Combine(temporary.FullName, "canonical.tar");
            await preparer.PrepareTarAsync(
                archive, "patch-template.tar.gz", "fix.sh",
                Path.Combine(temporary.FullName, "work"), canonical, CancellationToken.None);
            using var reader = new TarReader(File.OpenRead(canonical));
            var names = new List<string>();
            while (reader.GetNextEntry() is { } entry)
                names.Add(entry.Name);
            await Assert.That(names).Contains("noctf/fix/fix.sh");
            await Assert.That(names).Contains("noctf/fix/README.md");
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    [Test]
    public async Task V2Images_DeclareNumericNonRootUsersAndPrivateBuildContexts()
    {
        var kit = Path.Combine(TemplateRoot(), "starter-kits", "awdp");
        foreach (var role in new[] { "target", "checker" })
        {
            var dockerfile = await File.ReadAllTextAsync(Path.Combine(kit, "private", role, "Dockerfile"));
            await Assert.That(dockerfile).Contains("USER 10001:10001");
        }
        var smoke = await File.ReadAllTextAsync(Path.Combine(kit, "tests", "smoke.sh"));
        await Assert.That(smoke).Contains("$root/private/target");
        await Assert.That(smoke).Contains("$root/private/checker");
        await Assert.That(smoke).Contains("$root/private/artifacts/fixes");
        await Assert.That(smoke).DoesNotContain("$root/artifacts/fixes");
    }

    [Test]
    public async Task V2FillingGuide_UsesRealFrontendLabelsAndDisplayUnits()
    {
        var kit = Path.Combine(TemplateRoot(), "starter-kits", "awdp");
        var guide = await File.ReadAllTextAsync(Path.Combine(kit, "platform", "CONFIGURATION.md"));
        var fieldHelp = await File.ReadAllTextAsync(Path.Combine(kit, "platform", "FIELDS.md"));
        var hintHelp = await File.ReadAllTextAsync(Path.Combine(kit, "private", "HINTS.md"));
        var documentation = string.Join('\n', guide, fieldHelp, hintHelp);
        var client = Path.GetFullPath(Path.Combine(TemplateRoot(), "..", "..", "backend", "src", "NoCTF.API", "ClientApp", "app"));
        var components = new Dictionary<string, string[]>
        {
            ["components/views/admin/DefinitionContainerView.vue"] = ["镜像", "启动命令", "Flag 环境变量名", "对外端口", "内部端口", "环境变量", "标签", "禁止提权(no-new-privileges)", "只读根文件系统", "以非 root 用户运行", "移除的能力(cap-drop)", "增加的能力(cap-add)"],
            ["components/views/admin/DefinitionRuntimeView.vue"] = ["分配方式", "运行环境类型", "内存(MiB)", "CPU(核)", "进程数上限", "实例存活时间(秒)", "操作超时(秒)", "Flag 来源", "访问入口"],
            ["components/views/admin/DefinitionPatchSectionView.vue"] = ["补丁入口", "补丁应用命令", "补丁超时(秒)", "就绪超时(秒)", "Fix 包上传上限(MiB)"],
            ["components/views/admin/DefinitionCheckerSectionView.vue"] = ["启用 Fix 一次性验证 Checker", "向 Checker 提供 Fix 包"],
            ["components/views/admin/RunnerJobEditorView.vue"] = ["超时(秒)"],
            ["components/views/admin/UrlBindingListView.vue"] = ["显示模板", "暴露范围", "容器端口"],
            ["components/views/admin/ChallengeTemplateCreateDialogView.vue"] = ["标题", "游戏模式", "可见性", "方向", "题面"],
            ["components/views/page/admin/competitions/[id]/challenges/AdminCompetitionsByIdChallengesByCcIdPageView.vue"] = ["提示内容", "扣分", "发布时间(可选)"]
        };
        var localizedLabels = string.Join('\n', await Task.WhenAll(
            Directory.EnumerateFiles(
                    Path.Combine(client, "locales", "catalogs", "zh-CN"),
                    "*.ts",
                    SearchOption.TopDirectoryOnly)
                .Select(path => File.ReadAllTextAsync(path))));
        foreach (var component in components)
        {
            var source = string.Join('\n',
                await File.ReadAllTextAsync(Path.Combine(client, component.Key)),
                localizedLabels);
            foreach (var label in component.Value)
            {
                await Assert.That(source).Contains(label);
                await Assert.That(documentation).Contains(label);
            }
        }
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(DefinitionFixture()));
        var runtime = fixture.RootElement.GetProperty("runtime");
        await Assert.That(runtime.GetProperty("limits").GetProperty("memoryBytes").GetInt64() / (1024 * 1024)).IsEqualTo(256L);
        await Assert.That(runtime.GetProperty("limits").GetProperty("nanoCpus").GetInt64() / 1_000_000_000m).IsEqualTo(0.5m);
        await Assert.That(guide).Contains("| 内存(MiB) | `256` |");
        await Assert.That(guide).Contains("| CPU(核) | `0.5` |");
        await Assert.That(guide).Contains("第 1 行 `/bin/sh`；第 2 行 `{entrypoint}`");
        await Assert.That(hintHelp).Contains("留空不是立即发布");
    }

    [Test]
    public async Task V2AuthorDocuments_DoNotRequireConfigurationCodeOrInventTargetSnapshots()
    {
        var documents = Directory.EnumerateFiles(TemplateRoot(), "*.md", SearchOption.AllDirectories);
        foreach (var file in documents)
        {
            var text = await File.ReadAllTextAsync(file);
            await Assert.That(text).DoesNotContain("```json");
            await Assert.That(text).DoesNotContain("```yaml");
            await Assert.That(text).DoesNotContain("challenge.yml");
            await Assert.That(text).DoesNotContain("prepare-gitops.sh");
        }
        var checkerGuide = await File.ReadAllTextAsync(Path.Combine(TemplateRoot(), "starter-kits", "awdp", "private", "CHECKER-FIX.md"));
        await Assert.That(checkerGuide).Contains("向 Checker 提供 Fix 包");
        await Assert.That(checkerGuide).Contains("/noctf/fix");
        await Assert.That(checkerGuide).Contains("/tmp/noctf-fix-work");
        await Assert.That(checkerGuide).Contains("没有自动复制／挂载／下载接口");
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

    private static string DefinitionFixture() => Path.GetFullPath(Path.Combine(
        TemplateRoot(), "..", "..", "backend", "tests", "NoCTF.Tests", "Fixtures", "AuthoringV2", "awdp-definition.json"));

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
