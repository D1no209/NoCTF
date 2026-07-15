using System.Net;
using System.Text;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Container.K8s;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public sealed class KubernetesManagerConcurrencyTests
{
    private const string ComposeYaml = """
        services:
          web:
            image: nginx:alpine
            ports:
              - "8080"
        """;

    [Fact]
    public void InstanceNamespace_LongPrefixPreservesDistinctHashSuffix()
    {
        var prefix = new string('a', 63);

        var first = KubernetesNames.InstanceNamespace(prefix, "first-operation");
        var second = KubernetesNames.InstanceNamespace(prefix, "second-operation");

        Assert.True(first.Length <= 63);
        Assert.True(second.Length <= 63);
        Assert.NotEqual(first, second);
        Assert.StartsWith($"{KubernetesNames.InstancePrefix(prefix)}-", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeUpAsync_DifferentOperationOwner_DoesNotMutateNamespace()
    {
        var operationId = Guid.NewGuid();
        var otherOperationId = Guid.NewGuid();
        var config = new ComposeConfig("shared-project", ComposeYaml, OperationId: operationId);
        var requests = new List<(HttpMethod Method, string Path)>();
        using var provider = CreateProvider(request =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            if (request.Method == HttpMethod.Get &&
                request.RequestUri.AbsolutePath.StartsWith("/api/v1/namespaces/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Namespace(otherOperationId, "different-request"));
            }

            throw new InvalidOperationException($"Unexpected Kubernetes request: {request.Method} {request.RequestUri}");
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ComposeUpAsync(config));

        Assert.Contains("belongs to another Runner operation", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Method is not null && request.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task ComposeUpAsync_SameOperationDifferentFingerprint_IsRejectedBeforeMutation()
    {
        var operationId = Guid.NewGuid();
        var config = new ComposeConfig("shared-project", ComposeYaml, OperationId: operationId);
        var mutationCalls = 0;
        using var provider = CreateProvider(request =>
        {
            if (request.Method != HttpMethod.Get)
                mutationCalls++;
            return Json(HttpStatusCode.OK, Namespace(operationId, "different-request"));
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ComposeUpAsync(config));

        Assert.Contains("reused with a different request", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, mutationCalls);
    }

    [Fact]
    public async Task ComposeUpAsync_SameOperationNamespaceConflict_RecoversWinnerWithoutDelete()
    {
        var operationId = Guid.NewGuid();
        var config = new ComposeConfig("concurrent-project", ComposeYaml, OperationId: operationId);
        var fingerprint = KubernetesManager.ComputeComposeFingerprint(config);
        var namespaceReads = 0;
        var deploymentCreates = 0;
        var deleteCalls = 0;
        using var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get &&
                path.StartsWith("/api/v1/namespaces/", StringComparison.Ordinal) &&
                !path.EndsWith("/pods", StringComparison.Ordinal))
            {
                namespaceReads++;
                return namespaceReads == 1
                    ? Failure(HttpStatusCode.NotFound, "NotFound")
                    : Json(HttpStatusCode.OK, Namespace(operationId, fingerprint));
            }

            if (request.Method == HttpMethod.Post && path == "/api/v1/namespaces")
                return Failure(HttpStatusCode.Conflict, "AlreadyExists");

            if (request.Method == HttpMethod.Get && path.EndsWith("/pods", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, ReadyPods());

            if (request.Method == HttpMethod.Post)
            {
                if (path.Contains("/deployments", StringComparison.Ordinal))
                    deploymentCreates++;
                return Echo(request);
            }

            if (request.Method == HttpMethod.Delete)
            {
                deleteCalls++;
                return RawStatus(HttpStatusCode.OK, "Success");
            }

            throw new InvalidOperationException($"Unexpected Kubernetes request: {request.Method} {request.RequestUri}");
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);

        var result = await manager.ComposeUpAsync(config);

        Assert.Equal("running", result.Status);
        Assert.Equal(2, namespaceReads);
        Assert.Equal(1, deploymentCreates);
        Assert.Equal(0, deleteCalls);
    }

    [Fact]
    public async Task CreateContainerAsync_IncompleteSameOperation_IsIdempotentlyCompleted()
    {
        var operationId = Guid.NewGuid();
        var config = new ContainerConfig(
            Image: "nginx:alpine",
            NetworkAliases: ["gamebox"],
            OperationId: operationId);
        var fingerprint = KubernetesManager.ComputeContainerFingerprint(config);
        var deploymentCreates = 0;
        var deleteCalls = 0;
        using var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get &&
                path.StartsWith("/api/v1/namespaces/", StringComparison.Ordinal) &&
                !path.Contains("/pods", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Namespace(operationId, fingerprint));
            }

            if (request.Method == HttpMethod.Post)
            {
                if (path.Contains("/deployments", StringComparison.Ordinal))
                    deploymentCreates++;
                return Echo(request);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/pods", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, ReadyPods());

            if (request.Method == HttpMethod.Delete)
            {
                deleteCalls++;
                return RawStatus(HttpStatusCode.OK, "Success");
            }

            throw new InvalidOperationException($"Unexpected Kubernetes request: {request.Method} {request.RequestUri}");
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);

        var result = await manager.CreateContainerAsync(config);

        Assert.Equal("running", result.Status);
        Assert.Equal(1, deploymentCreates);
        Assert.Equal(0, deleteCalls);
    }

    [Fact]
    public async Task CreateContainerAsync_NamespacePreparationFailure_CleansCreatedNamespace()
    {
        var namespaceReads = 0;
        var deleteCalls = 0;
        using var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get &&
                path.StartsWith("/api/v1/namespaces/", StringComparison.Ordinal))
            {
                namespaceReads++;
                return Failure(HttpStatusCode.NotFound, "NotFound");
            }

            if (request.Method == HttpMethod.Post && path == "/api/v1/namespaces")
                return Echo(request);

            if (request.Method == HttpMethod.Post && path.Contains("/resourcequotas", StringComparison.Ordinal))
                return Failure(HttpStatusCode.InternalServerError, "InternalError");

            if (request.Method == HttpMethod.Delete)
            {
                deleteCalls++;
                return RawStatus(HttpStatusCode.OK, "Success");
            }

            throw new InvalidOperationException($"Unexpected Kubernetes request: {request.Method} {request.RequestUri}");
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => manager.CreateContainerAsync(new ContainerConfig(
            Image: "nginx:alpine")));

        Assert.Equal(1, namespaceReads);
        Assert.Equal(1, deleteCalls);
    }

    [Fact]
    public async Task ComposeDownAsync_MissingNamespace_IsIdempotent()
    {
        using var provider = CreateProvider(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Failure(HttpStatusCode.NotFound, "NotFound");
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);

        await manager.ComposeDownAsync(new ComposeDeployment(
            Id: Guid.NewGuid(),
            CompetitionId: Guid.NewGuid(),
            TeamId: null,
            ChallengeId: null,
            ProviderType: "kubernetes",
            ProjectName: "already-removed",
            ComposeYaml: ComposeYaml,
            Status: "running",
            StartedAt: DateTime.UtcNow,
            OrchestrationNamespace: "noctf-test-already-removed"));
    }

    [Fact]
    public async Task ComposeDownAsync_WaitsForDeletionBeforeImmediateNewOperation()
    {
        var oldOperationId = Guid.NewGuid();
        var newOperationId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var newConfig = new ComposeConfig("reset-project", ComposeYaml, OperationId: newOperationId);
        var namespaceName = KubernetesNames.InstanceNamespace("noctf-test", newConfig.ProjectName);
        var deleteRequested = false;
        var newNamespaceCreated = false;
        var deletionReads = 0;
        using var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/pods", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, ReadyPods());

            if (request.Method == HttpMethod.Get &&
                path.StartsWith("/api/v1/namespaces/", StringComparison.Ordinal))
            {
                if (!deleteRequested)
                    return Json(HttpStatusCode.OK, Namespace(
                        oldOperationId,
                        "old-request",
                        "old-uid",
                        "reset-project",
                        competitionId));
                if (newNamespaceCreated)
                {
                    return Json(HttpStatusCode.OK, Namespace(
                        newOperationId,
                        KubernetesManager.ComputeComposeFingerprint(newConfig),
                        "new-uid",
                        "reset-project",
                        competitionId));
                }

                deletionReads++;
                if (deletionReads == 1)
                {
                    var terminating = Namespace(
                        oldOperationId,
                        "old-request",
                        "old-uid",
                        "reset-project",
                        competitionId);
                    terminating.Metadata.DeletionTimestamp = DateTime.UtcNow;
                    return Json(HttpStatusCode.OK, terminating);
                }

                return Failure(HttpStatusCode.NotFound, "NotFound");
            }

            if (request.Method == HttpMethod.Delete)
            {
                deleteRequested = true;
                return RawStatus(HttpStatusCode.OK, "Success");
            }

            if (request.Method == HttpMethod.Post && path == "/api/v1/namespaces")
            {
                newNamespaceCreated = true;
                return Echo(request);
            }

            if (request.Method == HttpMethod.Post)
                return Echo(request);

            throw new InvalidOperationException($"Unexpected Kubernetes request: {request.Method} {request.RequestUri}");
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);
        var oldDeployment = new ComposeDeployment(
            Guid.NewGuid(),
            competitionId,
            null,
            null,
            "kubernetes",
            "reset-project",
            ComposeYaml,
            "running",
            DateTime.UtcNow,
            OrchestrationNamespace: namespaceName);

        await manager.ComposeDownAsync(oldDeployment);
        var result = await manager.ComposeUpAsync(newConfig);

        Assert.True(deletionReads >= 2);
        Assert.True(newNamespaceCreated);
        Assert.Equal("running", result.Status);
    }

    [Fact]
    public async Task RunContainerAsync_RestartRecoversStableJobAndUsesTimeoutExitCode()
    {
        var operationId = Guid.NewGuid();
        var config = new ContainerConfig(
            Image: "checker:latest",
            Ttl: TimeSpan.FromSeconds(5),
            OperationId: operationId);
        var fingerprint = KubernetesManager.ComputeContainerFingerprint(config);
        var jobName = $"job-{operationId:N}";
        var jobPostCalls = 0;
        var quotaCreates = 0;
        var deleteCalls = 0;
        var namespaceDeleted = false;
        using var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get &&
                path.StartsWith("/api/v1/namespaces/", StringComparison.Ordinal))
            {
                return namespaceDeleted
                    ? Failure(HttpStatusCode.NotFound, "NotFound")
                    : Json(HttpStatusCode.OK, Namespace(operationId, fingerprint, "runtime-uid"));
            }

            if (request.Method == HttpMethod.Get && path.Contains("/jobs/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, new V1Job
                {
                    Metadata = new V1ObjectMeta
                    {
                        Name = jobName,
                        CreationTimestamp = DateTime.UtcNow.AddMinutes(-1),
                        Labels = new Dictionary<string, string>
                        {
                            [KubernetesManifestFactory.OperationLabel] = operationId.ToString("D"),
                            [KubernetesManifestFactory.SpecFingerprintLabel] = fingerprint
                        }
                    }
                });
            }

            if (request.Method == HttpMethod.Post)
            {
                if (path.Contains("/jobs", StringComparison.Ordinal))
                    jobPostCalls++;
                if (path.Contains("/resourcequotas", StringComparison.Ordinal))
                    quotaCreates++;
                return Echo(request);
            }

            if (request.Method == HttpMethod.Patch)
                return Json(HttpStatusCode.OK, Namespace(operationId, fingerprint, "runtime-uid"));

            if (request.Method == HttpMethod.Delete)
            {
                deleteCalls++;
                namespaceDeleted = true;
                return RawStatus(HttpStatusCode.OK, "Success");
            }

            throw new InvalidOperationException($"Unexpected Kubernetes request: {request.Method} {request.RequestUri}");
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);

        var result = await manager.RunContainerAsync(config);

        Assert.Equal(jobName, result.ContainerId);
        Assert.Equal(124, result.ExitCode);
        Assert.Equal(0, jobPostCalls);
        Assert.Equal(1, quotaCreates);
        Assert.Equal(0, deleteCalls);
    }

    [Fact]
    public void JobManifest_HasServerSideDeadlineAndGarbageCollectionTtl()
    {
        var job = KubernetesManifestFactory.Job(
            "noctf-test-job",
            "checker",
            new ContainerConfig("checker:latest", Ttl: TimeSpan.FromSeconds(45)),
            new KubernetesRunnerOptions(),
            new OrchestrationSpec());

        Assert.Equal(45, job.Spec.ActiveDeadlineSeconds);
        Assert.Equal(1_800, job.Spec.TtlSecondsAfterFinished);
    }

    [Fact]
    public async Task CleanupExpiredRunReceipts_DeletesOnlyExpiredOwnedNamespaces()
    {
        var deleteCalls = 0;
        var operationId = Guid.NewGuid();
        var expired = RunReceiptNamespace(
            "noctf-test-expired",
            operationId,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var retained = RunReceiptNamespace(
            "noctf-test-retained",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(5));
        using var provider = CreateProvider(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/v1/namespaces")
                return Json(HttpStatusCode.OK, new V1NamespaceList { Items = [expired, retained] });
            if (request.Method == HttpMethod.Delete && path.EndsWith("/noctf-test-expired", StringComparison.Ordinal))
            {
                deleteCalls++;
                return RawStatus(HttpStatusCode.OK, "Success");
            }

            throw new InvalidOperationException($"Unexpected Kubernetes request: {request.Method} {request.RequestUri}");
        });
        var manager = new KubernetesManager(provider, NullLogger<KubernetesManager>.Instance);

        Assert.Equal(1, await manager.CleanupExpiredRunReceiptsAsync());
        Assert.Equal(1, deleteCalls);
    }

    private static KubernetesProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var configuration = new KubernetesClientConfiguration
        {
            Host = "http://localhost",
            SkipTlsVerify = true
        };
        var client = new Kubernetes(configuration, new DelegateHandler(responder));
        return new KubernetesProvider(client, new KubernetesRunnerOptions
        {
            NamespacePrefix = "noctf-test",
            ReadinessTimeoutSeconds = 10,
            DefaultExposure = "ClusterIP"
        });
    }

    private static V1Namespace Namespace(
        Guid operationId,
        string fingerprint,
        string? uid = null,
        string? projectName = null,
        Guid? competitionId = null)
    {
        var labels = new Dictionary<string, string>
        {
            [KubernetesManifestFactory.ManagedByLabel] = "noctf-runner",
            [KubernetesManifestFactory.OperationLabel] = operationId.ToString("D"),
            [KubernetesManifestFactory.SpecFingerprintLabel] = fingerprint
        };
        if (!string.IsNullOrWhiteSpace(projectName))
            labels[KubernetesManifestFactory.ProjectLabel] = KubernetesNames.SafeName(projectName);
        if (competitionId.HasValue)
            labels["competitionId"] = KubernetesNames.SafeName(competitionId.Value.ToString("D"));

        return new V1Namespace
        {
            Metadata = new V1ObjectMeta
            {
                Name = "test-namespace",
                Uid = uid,
                Labels = labels
            }
        };
    }

    private static V1Namespace RunReceiptNamespace(
        string name,
        Guid operationId,
        DateTimeOffset expiresAt)
        => new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Uid = Guid.NewGuid().ToString("N"),
                Labels = new Dictionary<string, string>
                {
                    [KubernetesManifestFactory.ManagedByLabel] = "noctf-runner",
                    [KubernetesManifestFactory.OperationLabel] = operationId.ToString("D"),
                    [KubernetesManifestFactory.SpecFingerprintLabel] = new string('a', 52),
                    [KubernetesManifestFactory.RunReceiptLabel] = "true",
                    [KubernetesManifestFactory.RunReceiptExpiresLabel] = expiresAt
                        .ToUnixTimeSeconds()
                        .ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            }
        };

    private static V1PodList ReadyPods()
        => new()
        {
            Items =
            [
                new V1Pod
                {
                    Metadata = new V1ObjectMeta { Name = "ready-pod" },
                    Status = new V1PodStatus
                    {
                        Phase = "Running",
                        Conditions = [new V1PodCondition { Type = "Ready", Status = "True" }]
                    }
                }
            ]
        };

    private static HttpResponseMessage Failure(HttpStatusCode statusCode, string reason)
        => new(statusCode)
        {
            Content = new StringContent(
                $$"""{"apiVersion":"v1","kind":"Status","status":"Failure","reason":"{{reason}}","code":{{(int)statusCode}}}""",
                Encoding.UTF8,
                "application/json")
        };

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object value)
        => new(statusCode)
        {
            Content = new StringContent(KubernetesJson.Serialize(value), Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Echo(HttpRequestMessage request)
        => new(HttpStatusCode.Created)
        {
            Content = new StringContent(
                request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}",
                Encoding.UTF8,
                "application/json")
        };

    private static HttpResponseMessage RawStatus(HttpStatusCode statusCode, string status)
        => new(statusCode)
        {
            Content = new StringContent(
                $$"""{"apiVersion":"v1","kind":"Status","status":"{{status}}","code":{{(int)statusCode}}}""",
                Encoding.UTF8,
                "application/json")
        };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
