namespace NoCTF.Tests.Architecture;

public sealed class SignalRArchitectureTests
{
    [Test]
    public async Task SignalR_publishers_use_typed_clients_without_changing_scale_out_topology()
    {
        var root = FindRepositoryRoot();
        var signalRRoot = Path.Combine(
            root, "backend", "src", "NoCTF.API", "SignalR");
        var sources = await Task.WhenAll(
            Directory.GetFiles(signalRRoot, "*.cs", SearchOption.AllDirectories)
                .Select(path => File.ReadAllTextAsync(path)));
        var registration = await File.ReadAllTextAsync(Path.Combine(
            root,
            "backend",
            "src",
            "NoCTF.API",
            "Composition",
            "ServiceRegistration.cs"));
        var hostRoles = await File.ReadAllTextAsync(Path.Combine(
            root,
            "backend",
            "src",
            "NoCTF.Hosting",
            "HostRoles.cs"));

        await Assert.That(sources.Any(source => source.Contains(".SendAsync("))).IsFalse();
        await Assert.That(sources.Any(source =>
            source.Contains("IHubContext<CompetitionHub>")
            || source.Contains("IHubContext<PlatformLogHub>"))).IsFalse();
        await Assert.That(registration).Contains("signalR.AddStackExchangeRedis(redis)");
        await Assert.That(registration).Contains("RedisLeaderboardRefreshRelay");
        await Assert.That(registration).Contains("RedisGameplayFactStateRelay");
        await Assert.That(hostRoles).Contains("return defaults ?? All()");
        await Assert.That(hostRoles).Contains("HostRole.Api, HostRole.Worker, HostRole.Runner");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
