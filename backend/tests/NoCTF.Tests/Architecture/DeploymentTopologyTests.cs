namespace NoCTF.Tests.Architecture;

public sealed class DeploymentTopologyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public async Task Deployment_manifests_support_composable_and_legacy_process_topologies()
    {
        var dockerfile = await ReadAsync("backend", "Dockerfile");
        var compose = await ReadAsync("deploy", "docker-compose.yml");
        var singleCompose = await ReadAsync("deploy", "docker-compose.single.yml");
        var hostProgram = await ReadAsync("backend", "src", "NoCTF.Host", "Program.cs");
        var roleModel = await ReadAsync("backend", "src", "NoCTF.Hosting", "HostRoles.cs");
        var routing = await ReadAsync("backend", "src", "NoCTF.Hosting", "MessageRouting.cs");
        var workerDeployment = Path.Combine(
            RepositoryRoot,
            "deploy",
            "k8s",
            "worker-deployment.yaml");
        var kubernetesConfig = await ReadAsync("deploy", "k8s", "configmap.yaml");

        await Assert.That(dockerfile).Contains("AS worker");
        await Assert.That(dockerfile).Contains("NoCTF.Worker.dll");
        await Assert.That(dockerfile).Contains("AS runtime");
        await Assert.That(dockerfile).Contains("libgssapi-krb5-2");
        await Assert.That(dockerfile).Contains("FROM runtime AS api");
        await Assert.That(dockerfile).Contains("FROM runtime AS worker");
        await Assert.That(dockerfile).Contains("FROM runtime AS runner");
        await Assert.That(dockerfile).Contains("FROM runtime AS host");
        await Assert.That(dockerfile).Contains("NoCTF.Host.dll");
        await Assert.That(compose).Contains("  worker:");
        await Assert.That(singleCompose).Contains("  noctf:");
        await Assert.That(singleCompose).Contains("target: host");
        await Assert.That(singleCompose).Contains("Hosting__Roles__0: Api");
        await Assert.That(compose).Contains("GET /health/ready HTTP/1.1");
        await Assert.That(compose).Contains("[[ \"$$status\" == *\" 200 \"* ]]");
        await Assert.That(File.Exists(workerDeployment)).IsTrue();
        await Assert.That(roleModel).Contains("public enum HostRole");
        await Assert.That(roleModel).Contains("HostRole.Api, HostRole.Worker, HostRole.Runner");
        await Assert.That(hostProgram).Contains("HostRoles.FromConfiguration");
        await Assert.That(hostProgram).Contains("ConfigureNoCtfWorkerMessaging");
        await Assert.That(hostProgram).Contains("ConfigureNoCtfRunnerMessaging");
        await Assert.That(routing)
            .Contains("PublishMessage<CompetitionEventCommitted>()");
        await Assert.That(routing)
            .Contains("ToPostgresqlQueue(\"noctf-worker\")");
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
                     "QqBot__PublicBaseUrl",
                     "Runtime__Docker__IngressProxy"
                 })
        {
            await Assert.That(compose).DoesNotContain(legacySetting);
            await Assert.That(kubernetesConfig).DoesNotContain(legacySetting);
        }
    }

    [Test]
    public async Task Deployment_manifests_use_role_aware_health_probes()
    {
        var dockerfile = await ReadAsync("backend", "Dockerfile");
        var compose = await ReadAsync("deploy", "docker-compose.yml");
        var backend = await ReadAsync("deploy", "k8s", "backend-deployment.yaml");
        var worker = await ReadAsync("deploy", "k8s", "worker-deployment.yaml");
        var runner = await ReadAsync("deploy", "k8s", "runner-deployment.yaml");
        var workerImageStage = dockerfile
            .Split("FROM runtime AS worker", 2, StringSplitOptions.None)[1]
            .Split("FROM runtime AS runner", 2, StringSplitOptions.None)[0];
        var workerComposeService = compose
            .Split("\n  worker:", 2, StringSplitOptions.None)[1]
            .Split("\n  runner:", 2, StringSplitOptions.None)[0];

        await Assert.That(workerImageStage).Contains("EXPOSE 8080");
        await Assert.That(workerComposeService).Contains("ASPNETCORE_URLS: http://+:8080");
        await Assert.That(workerComposeService).Contains("GET /health/ready HTTP/1.1");
        foreach (var manifest in new[] { backend, worker, runner })
        {
            await Assert.That(manifest).Contains("path: /health/live");
            await Assert.That(manifest).Contains("path: /health/ready");
        }
    }

    [Test]
    public async Task Stock_manifests_give_scoring_checkers_a_reachable_callback_identity()
    {
        var compose = await ReadAsync("deploy", "docker-compose.yml");
        var singleCompose = await ReadAsync("deploy", "docker-compose.single.yml");
        var kubernetesConfig = await ReadAsync("deploy", "k8s", "configmap.yaml");
        var backendDeployment = await ReadAsync(
            "deploy",
            "k8s",
            "backend-deployment.yaml");
        var networkPolicies = await ReadAsync("deploy", "k8s", "networkpolicy.yaml");

        await Assert.That(compose)
            .Contains("noctf.io/internal-role: scoring-callback-gateway");
        await Assert.That(compose)
            .Contains("Runtime__Docker__CallbackContainerLabelValue: scoring-callback-gateway");
        await Assert.That(compose)
            .Contains("RunnerScoring__CallbackBaseUrl: http://backend:8080");
        await Assert.That(singleCompose)
            .Contains("noctf.io/internal-role: scoring-callback-gateway");
        await Assert.That(singleCompose)
            .Contains("Runtime__Docker__CallbackContainerLabelValue: scoring-callback-gateway");
        await Assert.That(singleCompose)
            .Contains("RunnerScoring__CallbackBaseUrl: http://noctf:8080");

        await Assert.That(kubernetesConfig).Contains(
            "RunnerScoring__CallbackBaseUrl: \"http://backend-service.noctf.svc.cluster.local:8080\"");
        await Assert.That(kubernetesConfig).Contains(
            "Runtime__Kubernetes__CallbackNamespaceLabelValue: \"noctf\"");
        await Assert.That(kubernetesConfig).Contains(
            "Runtime__Kubernetes__CallbackPodLabelValue: \"scoring-callback-gateway\"");
        await Assert.That(backendDeployment)
            .Contains("noctf.io/internal-role: scoring-callback-gateway");
        await Assert.That(networkPolicies)
            .Contains("name: allow-scoring-callback-to-backend");
        await Assert.That(networkPolicies)
            .Contains("kubernetes.io/metadata.name: runtime");
        await Assert.That(networkPolicies).Contains("- awd-checker");
        await Assert.That(networkPolicies).Contains("- awdp-checker");
    }

    [Test]
    public async Task E2e_orchestrator_is_portable_and_uses_docker_assigned_ports()
    {
        var orchestrator = await ReadAsync("backend", "tests", "e2e.cs");
        var compose = await ReadAsync(
            "backend",
            "tests",
            "NoCTF.E2E",
            "docker-compose.ctf.yml");

        await Assert.That(orchestrator).Contains("#:property TargetFramework=net10.0");
        await Assert.That(orchestrator).Contains("[CallerFilePath]");
        await Assert.That(orchestrator).Contains("ProcessStartInfo");
        await Assert.That(orchestrator).Contains("ArgumentList.Add");
        await Assert.That(orchestrator).Contains("[\"port\", \"backend\", \"8080\"]");
        await Assert.That(orchestrator).Contains("API/Runner readiness timed out");
        await Assert.That(orchestrator).Contains("health={lastHealth}");
        await Assert.That(orchestrator).Contains("heartbeat={lastHeartbeat}");
        await Assert.That(orchestrator).DoesNotContain("wsl");
        await Assert.That(orchestrator).DoesNotContain(".ps1");
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
