using System.Text.RegularExpressions;

namespace NoCTF.Tests.Architecture;

public sealed class LiveSoloBoundaryTests
{
    [Test]
    public async Task Existing_mode_implementations_do_not_reference_live_solo_or_media_sdks()
    {
        var root = BackendRoot();
        var sourceFiles = new[] { "Ctf", "Awd", "Awdp", "Koh" }.SelectMany(mode => Directory.EnumerateFiles(
            Path.Combine(root, "src", "NoCTF.GameModes", mode), "*.cs", SearchOption.AllDirectories));
        var violations = sourceFiles.Where(path => Regex.IsMatch(File.ReadAllText(path), @"NoCTF\.(?:Domain|Application|GameModes|Infrastructure)\.LiveSolo|\bLiveKit\b"))
            .Select(path => Path.GetRelativePath(root, path)).ToArray();
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Provider_media_details_and_match_ids_do_not_enter_the_runtime_core()
    {
        var root = BackendRoot();
        var folders = new[] { "NoCTF.Domain/Runtime", "NoCTF.Application/Runtime", "NoCTF.Runtime.Docker", "NoCTF.Runtime.Kubernetes", "NoCTF.Runtime.Libvirt" };
        var violations = folders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, "src", folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"LiveSolo(?:Match|Round)Id|\bLiveKit\b"))
            .Select(path => Path.GetRelativePath(root, path)).ToArray();
        await Assert.That(violations).IsEmpty();
        var domain = File.ReadAllText(Path.Combine(root, "src", "NoCTF.Domain", "Runtime", "RuntimeInstance.cs"));
        await Assert.That(domain).Contains("Guid? ExecutionScopeId");
    }

    private static string BackendRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null) { if (File.Exists(Path.Combine(current.FullName, "NoCTF.slnx"))) return current.FullName; current = current.Parent; }
        throw new DirectoryNotFoundException("Backend root was not found.");
    }
}
