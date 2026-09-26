namespace NoCTF.Tests.Architecture;

public sealed class RedisBoundaryRulesTests
{
    [Test]
    public async Task Runtime_code_uses_Redis_only_through_FusionCache_adapters()
    {
        var root = FindRepositoryRoot();
        var sources = Directory.GetFiles(Path.Combine(root, "backend", "src"),
            "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj"
                + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var path in sources)
        {
            var source = await File.ReadAllTextAsync(path);
            await Assert.That(source.Contains("using StackExchange.Redis;", StringComparison.Ordinal))
                .IsFalse();
            await Assert.That(source.Contains("IConnectionMultiplexer", StringComparison.Ordinal))
                .IsFalse();
            await Assert.That(source.Contains("ScriptEvaluateAsync", StringComparison.Ordinal))
                .IsFalse();
        }
        var infrastructure = await File.ReadAllTextAsync(Path.Combine(root,
            "backend", "src", "NoCTF.Infrastructure", "NoCTF.Infrastructure.csproj"));
        await Assert.That(infrastructure)
            .DoesNotContain("<PackageReference Include=\"StackExchange.Redis\"");
        await Assert.That(File.Exists(Path.Combine(root,
            "deploy", "observability", "postgres-exporter", "queries.yml"))).IsFalse();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
