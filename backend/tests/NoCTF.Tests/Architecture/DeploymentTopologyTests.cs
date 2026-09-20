namespace NoCTF.Tests.Architecture;

public sealed class DeploymentTopologyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public async Task Deployment_manifests_support_composable_and_legacy_process_topologies()
    {
        var dockerfile = await ReadAsync("backend", "Dockerfile");
        var compose = await ReadAsync("deploy", "docker-compose.yml");
        var runtimeEnv = await ReadAsync("deploy", "env", "noctf", ".env.example");
        var healthcheck = await ReadAsync("backend", "docker", "healthcheck.sh");
        var apiProgram = await ReadAsync("backend", "src", "NoCTF.API", "Program.cs");
        var hostProgram = await ReadAsync("backend", "src", "NoCTF.Host", "Program.cs");
        var roleModel = await ReadAsync("backend", "src", "NoCTF.Hosting", "HostRoles.cs");
        var routing = await ReadAsync("backend", "src", "NoCTF.Hosting", "MessageRouting.cs");
        var persistence = await ReadAsync(
            "backend", "src", "NoCTF.Hosting", "WolverineHosting.cs");
        var outbox = await ReadAsync(
            "backend", "src", "NoCTF.Infrastructure", "Messaging",
            "WolverineTransactionalMessageOutbox.cs");
        var workerTopology = await ReadAsync(
            "backend", "src", "NoCTF.Worker",
            "WorkerMessageTopologyStartupValidator.cs");
        var runnerTopology = await ReadAsync(
            "backend", "src", "NoCTF.Runner", "RunnerRole.cs");
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
        await Assert.That(compose).Contains("  noctf:");
        await Assert.That(compose).DoesNotContain("build:");
        await Assert.That(compose).DoesNotContain("  migration:");
        await Assert.That(runtimeEnv).Contains("Hosting__Roles__0=Api");
        await Assert.That(runtimeEnv).Contains("Hosting__Roles__1=Worker");
        await Assert.That(runtimeEnv).Contains("Hosting__Roles__2=Runner");
        await Assert.That(hostProgram).Contains("DatabaseStartup.InitializeAsync");
        await Assert.That(apiProgram).Contains("DatabaseStartup.InitializeAsync");
        await Assert.That(apiProgram).Contains("Database:AutoMigrate");
        await Assert.That(healthcheck).Contains("GET /health/ready HTTP/1.1");
        await Assert.That(dockerfile).Contains("HEALTHCHECK");
        await Assert.That(File.Exists(workerDeployment)).IsTrue();
        await Assert.That(roleModel).Contains("public enum HostRole");
        await Assert.That(roleModel).Contains("HostRole.Api, HostRole.Worker, HostRole.Runner");
        await Assert.That(hostProgram).Contains("HostRoles.FromConfiguration");
        await Assert.That(hostProgram).Contains("ConfigureNoCtfWorkerMessaging");
        await Assert.That(hostProgram).Contains("ConfigureNoCtfRunnerMessaging");
        await Assert.That(routing)
            .Contains("options.Durability.MessageIdentity = MessageIdentity.IdAndDestination");
        await Assert.That(routing)
            .Contains("route.ToNatsSubject(NatsSubjects.RealtimeEvents)");
        await Assert.That(routing)
            .Contains("route.ToNatsSubject(NatsSubjects.LeaderboardEvents)");
        await Assert.That(persistence).Contains("PersistMessagesWithPostgresql");
        await Assert.That(persistence).Contains("UseEntityFrameworkCoreTransactions");
        await Assert.That(persistence)
            .Contains("new AwdpFixVerificationExecutionTimeoutPolicy()")
            .And.DoesNotContain("ExecutionTimeoutInSeconds = 60");
        await Assert.That(persistence).Contains("options.UseNats(nats)");
        await Assert.That(outbox).Contains("IDbContextOutbox<NoCtfDbContext>");
        await Assert.That(outbox).Contains("MultiFlushMode.AllowMultiples");
        await Assert.That(outbox).Contains("nats://subject/noctf.runner.");
        await Assert.That(workerTopology)
            .Contains("endpoint.BrokerRole, \"stream\"");
        await Assert.That(workerTopology)
            .Contains("typeof(SendEmailVerification)")
            .And.Contains("BackgroundEndpointAddress()");
        await Assert.That(routing)
            .Contains("Route<CompleteAwdpFixRecovery>(options, WorkerQueue.Control)");
        await Assert.That(routing)
            .Contains("Route<StartAwdpFixVerification>(options, WorkerQueue.Gameplay)");
        await Assert.That(routing)
            .Contains("Route<StartPatchVerification>(options, WorkerQueue.Gameplay)");
        await Assert.That(runnerTopology).Contains(".MaximumAckExtension(");
        await Assert.That(runnerTopology).DoesNotContain(".AckWait(");
        foreach (var workerQueue in new[]
                 {
                     "WorkerQueue.Control",
                     "WorkerQueue.Gameplay",
                     "WorkerQueue.Projection",
                     "WorkerQueue.Background"
                 })
        {
            await Assert.That(routing).Contains(workerQueue);
        }
        foreach (var runnerAvailabilitySetting in new[]
                 {
                     "Runner__Capacity__MemoryBytes",
                     "Runner__Capacity__NanoCpus",
                     "Runner__Capacity__PidsLimit",
                     "Runner__Heartbeat__IntervalSeconds",
                     "Runner__Heartbeat__TtlSeconds"
                 })
        {
            await Assert.That(runtimeEnv).Contains(runnerAvailabilitySetting);
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
        await Assert.That(dockerfile).Contains("HEALTHCHECK");
        await Assert.That(dockerfile).Contains("noctf-healthcheck");
        await Assert.That(dockerfile).DoesNotContain("EXPOSE ");
        await Assert.That(compose).DoesNotContain("healthcheck:");
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
        var runtimeEnv = await ReadAsync("deploy", "env", "noctf", ".env.example");
        var kubernetesConfig = await ReadAsync("deploy", "k8s", "configmap.yaml");
        var backendDeployment = await ReadAsync(
            "deploy",
            "k8s",
            "backend-deployment.yaml");
        var networkPolicies = await ReadAsync("deploy", "k8s", "networkpolicy.yaml");
        var pipeline = await ReadAsync(
            "backend",
            "src",
            "NoCTF.API",
            "Composition",
            "PipelineConfiguration.cs");

        await Assert.That(compose)
            .Contains("noctf.io/internal-role: scoring-callback-gateway");
        await Assert.That(runtimeEnv)
            .Contains("Runtime__Docker__CallbackContainerLabelValue=scoring-callback-gateway");
        await Assert.That(runtimeEnv)
            .Contains("RunnerScoring__CallbackBaseUrl=http://noctf:8080");
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
        await Assert.That(pipeline).DoesNotContain("UseHttpsRedirection");
    }

    [Test]
    public async Task Public_tls_terminates_at_nginx_while_internal_api_remains_http_only()
    {
        var compose = await ReadAsync("deploy", "docker-compose.yml");
        var runtimeEnv = await ReadAsync("deploy", "env", "noctf", ".env.example");
        var nginx = await ReadAsync("deploy", "nginx", "noctf.conf");
        var kubernetesConfig = await ReadAsync("deploy", "k8s", "configmap.yaml");
        var kubernetesIngress = await ReadAsync("deploy", "k8s", "ingress.yaml");
        var serviceRegistration = await ReadAsync(
            "backend", "src", "NoCTF.API", "Composition", "ServiceRegistration.cs");
        var pipeline = await ReadAsync(
            "backend", "src", "NoCTF.API", "Composition", "PipelineConfiguration.cs");

        await Assert.That(compose).Contains("name: 1panel-network");
        await Assert.That(compose).Contains("aliases: [noctf-web]");
        await Assert.That(compose).DoesNotContain("ports:");
        await Assert.That(compose).DoesNotContain("expose:");
        await Assert.That(runtimeEnv).Contains("ASPNETCORE_HTTP_PORTS=8080");
        await Assert.That(runtimeEnv).DoesNotContain("ASPNETCORE_URLS=");
        await Assert.That(runtimeEnv).Contains("ForwardedHeaders__KnownNetworks__0=");
        await Assert.That(runtimeEnv).Contains("ForwardedHeaders__AllowedHosts__0=");
        await Assert.That(nginx).Contains("return 308 https://$host$request_uri;");
        await Assert.That(nginx).Contains("proxy_pass http://127.0.0.1:8080;");
        await Assert.That(nginx).Contains("Strict-Transport-Security");
        await Assert.That(nginx).Contains("proxy_set_header X-Forwarded-Proto https;");
        await Assert.That(nginx).Contains("proxy_set_header X-Forwarded-For $remote_addr;");
        await Assert.That(nginx).DoesNotContain("$proxy_add_x_forwarded_for");
        await Assert.That(nginx).Contains("proxy_set_header Upgrade $http_upgrade;");
        await Assert.That(nginx).Contains("proxy_set_header Connection $connection_upgrade;");

        await Assert.That(kubernetesConfig).Contains("ASPNETCORE_HTTP_PORTS: \"8080\"");
        await Assert.That(kubernetesConfig).DoesNotContain("ASPNETCORE_URLS:");
        await Assert.That(kubernetesConfig).Contains("ForwardedHeaders__KnownNetworks__0:");
        await Assert.That(kubernetesConfig).DoesNotContain("ForwardedHeaders__TrustAll");
        await Assert.That(kubernetesIngress).Contains("ssl-redirect: \"true\"");
        await Assert.That(kubernetesIngress).Contains("force-ssl-redirect: \"true\"");
        await Assert.That(kubernetesIngress).Contains("proxy_set_header Upgrade $http_upgrade;");

        await Assert.That(serviceRegistration).Contains("AddNoCtfForwardedHeaders(configuration)");
        await Assert.That(pipeline).Contains("app.UseForwardedHeaders();");
        await Assert.That(pipeline).DoesNotContain("UseHttpsRedirection");
    }

    [Test]
    public async Task Kubernetes_platform_egress_is_explicit_and_fail_closed()
    {
        var networkPolicies = await ReadAsync("deploy", "k8s", "networkpolicy.yaml");
        var smtpExample = await ReadAsync("deploy", "k8s", "smtp-egress.example.yaml");
        var runnerRbac = await ReadAsync("deploy", "k8s", "runner-rbac.yaml");

        var runnerApiPolicy = networkPolicies
            .Split("\n---", StringSplitOptions.RemoveEmptyEntries)
            .Single(document => document.Contains(
                "name: runner-to-kubernetes-api",
                StringComparison.Ordinal));
        await Assert.That(runnerApiPolicy).Contains("kind: CiliumNetworkPolicy");
        await Assert.That(runnerApiPolicy).Contains("- kube-apiserver");
        await Assert.That(runnerApiPolicy).DoesNotContain("0.0.0.0/0");

        var clusterDnsPolicy = networkPolicies
            .Split("\n---", StringSplitOptions.RemoveEmptyEntries)
            .Single(document => document.Contains(
                "name: allow-cluster-dns-egress",
                StringComparison.Ordinal));
        await Assert.That(clusterDnsPolicy).Contains("kind: CiliumNetworkPolicy");
        await Assert.That(clusterDnsPolicy).Contains("\"k8s:k8s-app\": kube-dns");
        await Assert.That(clusterDnsPolicy).Contains("rules:");
        await Assert.That(clusterDnsPolicy).Contains("dns:");
        await Assert.That(clusterDnsPolicy).Contains("matchPattern: \"*.svc.cluster.local\"");

        await Assert.That(smtpExample).Contains("kind: CiliumNetworkPolicy");
        await Assert.That(smtpExample).Contains("matchName: smtp.example.com");
        await Assert.That(smtpExample).Contains("\"k8s:k8s-app\": kube-dns");
        await Assert.That(smtpExample).Contains("rules:");
        await Assert.That(smtpExample).Contains("dns:");
        await Assert.That(smtpExample).DoesNotContain("matchPattern:");
        await Assert.That(smtpExample).DoesNotContain("0.0.0.0/0");
        await Assert.That(smtpExample).DoesNotContain("password");
        await Assert.That(runnerRbac).Contains("kind: ClusterRole");
        await Assert.That(runnerRbac).Contains("name: noctf-runner-node-attestation");
        await Assert.That(runnerRbac).Contains("resources: [\"nodes\"]");
        await Assert.That(runnerRbac).Contains("verbs: [\"get\", \"list\"]");
    }

    [Test]
    public async Task Kubernetes_installation_enforces_network_policies_before_workloads()
    {
        var readme = await ReadAsync("deploy", "k8s", "k8s-readme.md");
        var networkPolicyIndex = readme.IndexOf(
            "kubectl apply -f networkpolicy.yaml",
            StringComparison.Ordinal);

        await Assert.That(networkPolicyIndex).IsGreaterThanOrEqualTo(0);
        foreach (var workload in new[]
                 {
                     "postgres-deployment.yaml",
                     "redis-deployment.yaml",
                     "minio-deployment.yaml",
                     "migration-job.yaml",
                     "backend-deployment.yaml",
                     "worker-deployment.yaml",
                     "runner-deployment.yaml"
                 })
        {
            await Assert.That(readme.IndexOf(
                $"kubectl apply -f {workload}",
                StringComparison.Ordinal)).IsGreaterThan(networkPolicyIndex);
        }

        await Assert.That(readme).Contains(
            "Do not replace the staged sequence with a single directory-wide apply.");
    }

    [Test]
    public async Task Kubernetes_manifests_use_only_expected_api_groups()
    {
        var standardApiVersions = new HashSet<string>(StringComparer.Ordinal)
        {
            "v1",
            "apps/v1",
            "batch/v1",
            "networking.k8s.io/v1",
            "rbac.authorization.k8s.io/v1"
        };
        var kubernetesManifestContents = await Task.WhenAll(
            Directory.GetFiles(
                    Path.Combine(RepositoryRoot, "deploy", "k8s"),
                    "*.yaml",
                    SearchOption.TopDirectoryOnly)
                .Select(path => File.ReadAllTextAsync(path)));
        var customResources = kubernetesManifestContents
            .SelectMany(content => System.Text.RegularExpressions.Regex.Matches(
                content,
                "(?m)^apiVersion: (?<apiVersion>\\S+)\\r?\\nkind: (?<kind>\\S+)"))
            .Where(match => !standardApiVersions.Contains(
                match.Groups["apiVersion"].Value))
            .Select(match =>
                $"{match.Groups["apiVersion"].Value}:{match.Groups["kind"].Value}")
            .ToArray();
        await Assert.That(customResources).Count().IsEqualTo(5);
        await Assert.That(customResources.All(resource =>
            resource == "cilium.io/v2:CiliumNetworkPolicy")).IsTrue();
    }

    [Test]
    public async Task Ci_publishes_one_verified_host_image_to_configured_registries()
    {
        var ci = (await ReadAsync(".github", "workflows", "ci.yml"))
            .ReplaceLineEndings("\n");
        var publishJob = System.Text.RegularExpressions.Regex.Match(
            ci,
            "(?ms)^  publish-images:\n(?<body>.*?)(?=^  deploy-test:)")
            .Groups["body"]
            .Value;

        await Assert.That(ci).Contains("  publish-images:\n");
        await Assert.That(publishJob).IsNotEmpty();
        await Assert.That(ci).DoesNotContain("  verify:\n");
        await Assert.That(publishJob).DoesNotContain("    needs:");
        await Assert.That(ci).DoesNotContain("bun test");
        await Assert.That(ci).DoesNotContain("dotnet test");
        await Assert.That(ci).DoesNotContain("bash deploy/recovery/rehearse.sh");
        await Assert.That(ci).Contains("    branches:\n      - main\n");
        await Assert.That(ci).Contains("      packages: write\n");
        await Assert.That(ci).Contains("ghcr.io/$repository_owner/$repository_name");
        await Assert.That(ci).Contains("CUSTOM_REGISTRY: ${{ vars.CUSTOM_REGISTRY }}");
        await Assert.That(ci).Contains("if: env.CUSTOM_REGISTRY != ''");
        await Assert.That(ci).Contains("target: host");
        await Assert.That(ci).Contains("steps.build.outputs.digest");
        await Assert.That(ci).Contains("deployment_repository");
        await Assert.That(ci).Contains("push: true");
        await Assert.That(ci).Contains("provenance: false");
        await Assert.That(ci).Contains("sbom: false");
        await Assert.That(ci).Contains(
            "cache-to: type=gha,mode=max,scope=noctf-host,ignore-error=true");
        await Assert.That(ci).DoesNotContain("matrix.image");
        await Assert.That(ci).DoesNotContain("matrix.target");

    }

    [Test]
    public async Task Ci_deploys_the_exact_main_commit_after_image_publication()
    {
        var ci = (await ReadAsync(".github", "workflows", "ci.yml"))
            .ReplaceLineEndings("\n");

        await Assert.That(ci).Contains("  deploy-test:\n");
        await Assert.That(ci).Contains("    name: Deploy test server\n");
        await Assert.That(ci).Contains("    needs: publish-images\n");
        await Assert.That(ci).Contains(
            "    if: github.event_name == 'push' && github.ref == 'refs/heads/main'\n");
        await Assert.That(ci).Contains("    environment: production\n");
        await Assert.That(ci).Contains("      group: noctf-test-deploy\n");
        await Assert.That(ci).Contains("      cancel-in-progress: false\n");
        await Assert.That(ci).Contains("NOCTF_DEPLOY_HOST");
        await Assert.That(ci).Contains("NOCTF_DEPLOY_SSH_KEY");
        await Assert.That(ci).Contains("NOCTF_DEPLOY_KNOWN_HOSTS");
        await Assert.That(ci).Contains("DEPLOY_IMAGE: ${{ needs.publish-images.outputs.deployment-image }}");
        await Assert.That(ci).Contains("Authenticate deployment registry");
        await Assert.That(ci).Contains("Deploy exact image");
        await Assert.That(ci).Contains("git archive \\\n");
        await Assert.That(ci).Contains("$GITHUB_SHA");
        await Assert.That(ci).Contains("backend/Directory.Build.props");
        await Assert.That(ci).Contains("deploy/ci/update_image.py");
        await Assert.That(ci).DoesNotContain("            \"$GITHUB_SHA\"\n          tar -tzf");
        await Assert.That(ci).Contains("deploy/ci/deploy.sh");
        await Assert.That(ci).Contains("noctf-ci-upload-$GITHUB_SHA");
        await Assert.That(ci).Contains("172.26.106.246|noctf.d1no.cn)");
        await Assert.That(ci).Contains("remote_hostname=$(ssh noctf-deploy hostname)");
        await Assert.That(ci).Contains("\"${remote_hostname,,}\" == dino209");
        await Assert.That(ci).Contains("if: always() && steps.verify_test_target.outcome == 'success'");
        await Assert.That(ci).DoesNotContain("kompose_version=1.38.0");
        await Assert.That(ci).DoesNotContain("Reused the verified Kompose asset");
    }

    [Test]
    public async Task External_deployment_artifacts_are_immutable_and_verified()
    {
        var dockerfile = await ReadAsync("backend", "Dockerfile");
        var deployScript = await ReadAsync("deploy", "ci", "deploy.sh")
            + await ReadAsync("deploy", "ci", "update_image.py");
        var deploymentFiles = new[]
        {
            await ReadAsync("deploy", "docker-compose.yml"),
            await ReadAsync("deploy", ".env.example"),
            await ReadAsync("deploy", "k8s", "postgres-deployment.yaml"),
            await ReadAsync("deploy", "k8s", "redis-deployment.yaml"),
            await ReadAsync("deploy", "k8s", "minio-deployment.yaml"),
            await ReadAsync("deploy", "k8s", "minio-init-job.yaml")
        };

        var externalBaseImages = dockerfile.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("FROM ", StringComparison.Ordinal)
                && !line.StartsWith("FROM runtime ", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(externalBaseImages).Count().IsEqualTo(5);
        await Assert.That(externalBaseImages.All(line =>
            System.Text.RegularExpressions.Regex.IsMatch(
                line,
                "^FROM [^ ]+@sha256:[a-f0-9]{64} AS [^ ]+$"))).IsTrue();
        await Assert.That(dockerfile).Contains(
            "FROM docker.m.daocloud.io/oven/bun:1.3.14@sha256:");
        await Assert.That(dockerfile).Contains(
            "FROM docker.m.daocloud.io/docker:28.5.1-cli@sha256:");
        await Assert.That(dockerfile).DoesNotContain(
            "FROM docker:28.5.1-cli@sha256:");
        await Assert.That(dockerfile).Contains("sha256sum -c -");
        await Assert.That(dockerfile).Contains("KOMPOSE_SHA256=");
        await Assert.That(dockerfile).Contains("https://archive.ubuntu.com/ubuntu");
        await Assert.That(dockerfile).Contains("https://security.ubuntu.com/ubuntu");
        await Assert.That(dockerfile).Contains("--retry-all-errors");
        await Assert.That(dockerfile).Contains("--speed-time 30");
        await Assert.That(dockerfile).Contains("COPY backend/docker-assets/");
        await Assert.That(deployScript).Contains(
            "platform_image=${5:?published platform image is required}");
        await Assert.That(deployScript).Contains("docker-compose.yml");
        await Assert.That(deployScript).Contains("docker pull \"$platform_image\"");
        await Assert.That(deployScript).Contains("--no-build");
        await Assert.That(deployScript).DoesNotContain("run --rm migration");
        await Assert.That(deployScript).Contains("def schema_fingerprint(postgres)");
        await Assert.That(deployScript).Contains("validate_existing_config(config, apps)");
        await Assert.That(deployScript).DoesNotContain("run --rm --pull");
        await Assert.That(deployScript).DoesNotContain("kompose_asset_path");
        await Assert.That(deployScript).DoesNotContain(
            "\"${compose[@]}\" build migration backend worker runner");

        var externalRuntimeImages = deploymentFiles
            .SelectMany(content => content.Split('\n'))
            .Select(line => line.Trim())
            .Select(line => line.Contains("_IMAGE=", StringComparison.Ordinal) ? "image: " + line[(line.IndexOf('=') + 1)..] : line)
            .Where(line => line.StartsWith("image: postgres:", StringComparison.Ordinal)
                || line.StartsWith("image: redis:", StringComparison.Ordinal)
                || line.StartsWith(
                    "image: docker.m.daocloud.io/library/nats:",
                    StringComparison.Ordinal)
                || line.StartsWith("image: minio/", StringComparison.Ordinal)
                || line.StartsWith("image: registry:", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(externalRuntimeImages).Count().IsEqualTo(8);
        await Assert.That(externalRuntimeImages.All(line =>
            System.Text.RegularExpressions.Regex.IsMatch(
                line,
                "^image: [^ ]+@sha256:[a-f0-9]{64}$"))).IsTrue();
        await Assert.That(externalRuntimeImages.Any(line =>
            line.Contains(":latest", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Production_deployment_never_starts_exporters_or_changes_other_services()
    {
        var script = await ReadAsync("deploy", "ci", "deploy.sh")
            + await ReadAsync("deploy", "ci", "update_image.py");
        await Assert.That(script).Contains("docker pull \"$platform_image\"");
        await Assert.That(script).Contains("\"--no-deps\", \"--no-build\", \"--pull\", \"never\"");
        await Assert.That(script).DoesNotContain("prune --");
        await Assert.That(script).DoesNotContain("ensure_observability_images");
        await Assert.That(script).DoesNotContain("nginx -s");
        await Assert.That(script).DoesNotContain("--remove-orphans");
        await Assert.That(script).Contains("refusing a new empty deployment");
        await Assert.That(script).Contains("No automatic database rollback");
        await Assert.That(script).Contains("CI must never deploy to production");
        await Assert.That(script).Contains("Network identity changed:");
        await Assert.That(script).DoesNotContain("\"network\", \"create\"");
        await Assert.That(script).DoesNotContain("\"network\", \"rm\"");
    }

    [Test]
    public async Task Production_defaults_disable_telemetry_and_remove_old_overlays()
    {
        var environment = await ReadAsync("deploy", "env", "noctf", ".env.example");
        var registryEnvironment = await ReadAsync("deploy", "env", "registry", ".env.example");
        await Assert.That(environment).Contains("Observability__Enabled=false");
        await Assert.That(environment).Contains("OTEL_SDK_DISABLED=true");
        await Assert.That(registryEnvironment).Contains("OTEL_TRACES_EXPORTER=none");
        foreach (var obsolete in new[] { "docker-compose.single.yml", "docker-compose.ci.yml", "docker-compose.observability.yml" })
            await Assert.That(File.Exists(Path.Combine(RepositoryRoot, "deploy", obsolete))).IsFalse();
    }

    [Test]
    public async Task Optional_monitoring_code_does_not_enable_exporters_when_disabled()
    {
        var extension = await ReadAsync("backend", "src", "NoCTF.Hosting", "Observability", "ObservabilityExtensions.cs");
        var infrastructure = await ReadAsync("backend", "src", "NoCTF.Infrastructure", "ServiceRegistration.cs");
        var environment = await ReadAsync("deploy", "env", "noctf", ".env.example");
        await Assert.That(extension).Contains("Observability:Enabled");
        await Assert.That(infrastructure).Contains("Observability:Enabled");
        await Assert.That(environment).Contains("Observability__PrometheusBaseUrl=");
        await Assert.That(environment).DoesNotContain("http://prometheus");
    }

    [Test]
    public async Task Ci_and_test_infrastructure_dependencies_are_immutable()
    {
        var ci = await ReadAsync(".github", "workflows", "ci.yml");
        var actionReferences = ci.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- uses:", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(actionReferences).Count().IsEqualTo(2);
        await Assert.That(actionReferences.All(line =>
            System.Text.RegularExpressions.Regex.IsMatch(
                line,
                "^- uses: [a-z0-9-]+/[a-z0-9-]+@[a-f0-9]{40} # v[0-9]+$"))).IsTrue();

        await Assert.That(actionReferences.Count(line =>
            line.StartsWith("- uses: actions/checkout@", StringComparison.Ordinal))).IsEqualTo(2);

        var ciServiceImages = ci.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("image: postgres:", StringComparison.Ordinal)
                || line.StartsWith("image: redis:", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(ciServiceImages).Count().IsEqualTo(0);
        await Assert.That(ciServiceImages.All(line => IsImmutableTestImage(line["image: ".Length..])))
            .IsTrue();

        var e2eDirectory = Path.Combine(RepositoryRoot, "backend", "tests", "NoCTF.E2E");
        var e2eComposeFiles = Directory.GetFiles(
                e2eDirectory,
                "docker-compose.*.yml",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        await Assert.That(e2eComposeFiles).Count().IsEqualTo(4);
        var e2eComposeContents = await Task.WhenAll(
            e2eComposeFiles.Select(path => File.ReadAllTextAsync(path)));
        await Assert.That(e2eComposeContents.All(content =>
                content.Contains("ASPNETCORE_HTTP_PORTS: \"8080\"", StringComparison.Ordinal)
                && !content.Contains("ASPNETCORE_URLS:", StringComparison.Ordinal)))
            .IsTrue();
        var e2eServiceImages = e2eComposeContents
            .SelectMany(content => content.Split('\n'))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("image: postgres:", StringComparison.Ordinal)
                || line.StartsWith("image: redis:", StringComparison.Ordinal)
                || line.StartsWith("image: minio/minio:", StringComparison.Ordinal))
            .Select(line => line["image: ".Length..])
            .ToArray();
        await Assert.That(e2eServiceImages).Count().IsEqualTo(16);
        await Assert.That(e2eServiceImages.All(IsImmutableTestImage)).IsTrue();

        var e2eRunnerDockerfile = await ReadAsync(
            "backend", "tests", "NoCTF.E2E", "Dockerfile.runner");
        var e2eRunnerBaseImages = e2eRunnerDockerfile.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("FROM ", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(e2eRunnerBaseImages).Count().IsEqualTo(3);
        await Assert.That(e2eRunnerBaseImages.All(line =>
            System.Text.RegularExpressions.Regex.IsMatch(
                line,
                "^FROM [^ ]+:[^ @]+@sha256:[a-f0-9]{64}(?: AS [^ ]+)?$"))).IsTrue();

        var testSourceFiles = Directory.GetFiles(
                Path.Combine(RepositoryRoot, "backend", "tests", "NoCTF.Tests"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Architecture{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToArray();
        var testSources = string.Join('\n', await Task.WhenAll(
            testSourceFiles.Select(path => File.ReadAllTextAsync(path))));
        var builderCount = System.Text.RegularExpressions.Regex.Matches(
            testSources,
            "new (?:PostgreSqlBuilder|RedisBuilder)\\(").Count;
        var pinnedBuilders = System.Text.RegularExpressions.Regex.Matches(
            testSources,
            "new (?:PostgreSqlBuilder|RedisBuilder)\\(\\s*\"(?<image>[^\"]+)\"");
        await Assert.That(builderCount).IsGreaterThan(0);
        await Assert.That(pinnedBuilders).Count().IsEqualTo(builderCount);
        await Assert.That(pinnedBuilders
            .Select(match => match.Groups["image"].Value)
            .All(IsImmutableTestImage)).IsTrue();
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

    private static bool IsImmutableTestImage(string image) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            image,
            "^(?:postgres|redis|minio/minio):[A-Za-z0-9._-]+@sha256:[a-f0-9]{64}$");

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
