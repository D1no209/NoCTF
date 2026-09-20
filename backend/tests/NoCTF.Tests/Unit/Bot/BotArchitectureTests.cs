namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class BotArchitectureTests
{
    [Test]
    public async Task Bot_RemainsAProtocolOnlyConsumerWithoutCoreProjectReferences()
    {
        var root = FindRepositoryRoot();
        var projectRoots = new[]
        {
            Path.Combine(root, "backend", "src", "NoCTF.Bot"),
            Path.Combine(root, "backend", "src", "NoCTF.Bot.Core"),
            Path.Combine(root, "backend", "src", "NoCTF.Bot.Providers.Milky")
        };
        var projects = string.Join('\n', projectRoots.SelectMany(projectRoot =>
            Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly))
            .Select(File.ReadAllText));
        var source = string.Join('\n', projectRoots.SelectMany(projectRoot => Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        var coreSource = string.Join('\n', Directory
            .EnumerateFiles(projectRoots[1], "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        await Assert.That(projects).DoesNotContain("NoCTF.Application.csproj");
        await Assert.That(projects).DoesNotContain("NoCTF.Infrastructure.csproj");
        await Assert.That(projects).DoesNotContain("NoCTF.Domain.csproj");
        await Assert.That(projects).DoesNotContain("NoCTF.API.csproj");
        await Assert.That(source).DoesNotContain("NoCTF.Application");
        await Assert.That(source).DoesNotContain("NoCTF.Infrastructure");
        await Assert.That(source).DoesNotContain("NoCTF.Domain");
        await Assert.That(source).DoesNotContain("/api/v1/admin");
        await Assert.That(coreSource).DoesNotContain("Milky");
        await Assert.That(coreSource).DoesNotContain("QQ");
    }

    [Test]
    public async Task Platform_DoesNotReferenceBotOrChatProviderImplementations()
    {
        var root = FindRepositoryRoot();
        var platformRoots = new[]
        {
            "NoCTF.Domain",
            "NoCTF.Application",
            "NoCTF.Infrastructure",
            "NoCTF.API",
            "NoCTF.Worker",
            "NoCTF.Runner",
            "NoCTF.Hosting",
            "NoCTF.Host"
        }.Select(name => Path.Combine(root, "backend", "src", name));
        var source = string.Join('\n', platformRoots.SelectMany(projectRoot => Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        var projects = string.Join('\n', platformRoots.SelectMany(projectRoot =>
            Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly))
            .Select(File.ReadAllText));

        await Assert.That(projects).DoesNotContain("NoCTF.Bot");
        await Assert.That(source).DoesNotContain("NoCTF.Bot");
        await Assert.That(source).DoesNotContain("Milky");
        await Assert.That(source).DoesNotContain("OneBot");
        await Assert.That(source).DoesNotContain("UniQsign");
        await Assert.That(source).DoesNotContain("QQ");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            current = current.Parent;
        return current?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }
}
