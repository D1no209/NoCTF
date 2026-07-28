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
        await Assert.That(compose).Contains("GET /health HTTP/1.1");
        await Assert.That(compose).Contains("GET /health/ready HTTP/1.1");
        await Assert.That(compose).Contains("[[ \"$$status\" == *\" 200 \"* ]]");
        await Assert.That(File.Exists(workerDeployment)).IsTrue();
        await Assert.That(workerProgram)
            .Contains("options.Discovery.IncludeType(typeof(BackendMessageHandlers));");
        await Assert.That(workerProgram)
            .Contains("options.PublishMessage<ProjectLeaderboard>().ToPostgresqlQueue(\"noctf-worker\");");
        foreach (var runnerAvailabilitySetting in new[]
                 {
                     "Runner__Capacity__MemoryBytes",
                     "Runner__Capacity__NanoCpus",
                     "Runner__Capacity__PidsLimit",
                     "Runner__Heartbeat__IntervalSeconds",
                     "Runner__Heartbeat__TtlSeconds"
                 })
        {
            await Assert.That(compose).Contains(runnerAvailabilitySetting);
            await Assert.That(kubernetesConfig).Contains(runnerAvailabilitySetting);
        }

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

    [Test]
    public async Task Ctf_e2e_readiness_bypasses_proxy_and_normalizes_redis_output()
    {
        var script = await ReadAsync("backend", "scripts", "Run-CtfE2E.ps1");
        var compose = await ReadAsync(
            "backend",
            "tests",
            "NoCTF.E2E",
            "docker-compose.ctf.yml");

        await Assert.That(script).Contains("$httpHandler.UseProxy = $false");
        await Assert.That(script).Contains("($heartbeat | Out-String).Trim()");
        await Assert.That(script).Contains("Last API status");
        await Assert.That(script).Contains("Last Redis heartbeat output");
        await Assert.That(script).Contains("function Resolve-E2EApiPort");
        await Assert.That(script).Contains("port backend 8080");
        await Assert.That(script).Contains("$env:NO_PROXY = \"127.0.0.1,localhost\"");
        await Assert.That(script).DoesNotContain("wsl");
        await Assert.That(compose).Contains("\"127.0.0.1::8080\"");
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
