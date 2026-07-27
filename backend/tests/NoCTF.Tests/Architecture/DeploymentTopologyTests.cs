namespace NoCTF.Tests.Architecture;

public sealed class DeploymentTopologyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public async Task Deployment_manifests_host_the_three_production_processes_without_legacy_runner_http_configuration()
    {
        var dockerfile = await ReadAsync("backend", "Dockerfile");
        var compose = await ReadAsync("deploy", "docker-compose.yml");
        var workerProgram = await ReadAsync("backend", "src", "NoCTF.Worker", "Program.cs");
        var workerDeployment = Path.Combine(
            RepositoryRoot,
            "deploy",
            "k8s",
            "worker-deployment.yaml");
        var kubernetesConfig = await ReadAsync("deploy", "k8s", "configmap.yaml");

        await Assert.That(dockerfile).Contains("AS worker");
        await Assert.That(dockerfile).Contains("NoCTF.Worker.dll");
        await Assert.That(compose).Contains("  worker:");
        await Assert.That(File.Exists(workerDeployment)).IsTrue();
        await Assert.That(workerProgram)
            .Contains("options.Discovery.IncludeType(typeof(BackendMessageHandlers));");

        foreach (var legacySetting in new[]
                 {
                     "Runner__ApiKey",
                     "Runner__BaseUrl",
                     "QqBot__PublicBaseUrl"
                 })
        {
            await Assert.That(compose).DoesNotContain(legacySetting);
            await Assert.That(kubernetesConfig).DoesNotContain(legacySetting);
        }
    }

    private static Task<string> ReadAsync(params string[] segments) =>
        File.ReadAllTextAsync(Path.Combine([RepositoryRoot, .. segments]));

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
