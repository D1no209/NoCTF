namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class BotArchitectureTests
{
    [Test]
    public async Task Bot_RemainsAProtocolOnlyConsumerWithoutCoreProjectReferences()
    {
        var root = FindRepositoryRoot();
        var projectRoot = Path.Combine(root, "backend", "src", "NoCTF.Bot");
        var project = await File.ReadAllTextAsync(Path.Combine(projectRoot, "NoCTF.Bot.csproj"));
        var source = string.Join('\n', Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        await Assert.That(project).DoesNotContain("ProjectReference");
        await Assert.That(source).DoesNotContain("NoCTF.Application");
        await Assert.That(source).DoesNotContain("NoCTF.Infrastructure");
        await Assert.That(source).DoesNotContain("NoCTF.Domain");
        await Assert.That(source).DoesNotContain("/api/v1/admin");
        await Assert.That(source).DoesNotContain("/api/v1/notifications");
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
