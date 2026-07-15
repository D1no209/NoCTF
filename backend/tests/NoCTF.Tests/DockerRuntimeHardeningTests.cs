using System.Net;
using System.Reflection;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Container.Docker;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public sealed class DockerRuntimeHardeningTests
{
    [Fact]
    public async Task CreateContainerAsync_RestartRejectsOperationIdReusedWithDifferentRequest()
    {
        var operationId = Guid.NewGuid();
        var original = new ContainerConfig(
            "gamebox:latest",
            Command: "serve --port 8080",
            OperationId: operationId);
        var originalFingerprint = DockerManager.ComputeRunFingerprint(original);
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                [new ContainerListResponse { ID = "existing", Created = DateTime.UtcNow.AddMinutes(-1) }]),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(new ContainerInspectResponse
            {
                ID = "existing",
                Created = DateTime.UtcNow.AddMinutes(-1),
                Config = new Config
                {
                    Image = original.Image,
                    Labels = new Dictionary<string, string>
                    {
                        [DockerProvider.ManagedLabel] = bool.TrueString,
                        [DockerProvider.OperationLabel] = operationId.ToString("D"),
                        [DockerProvider.RequestFingerprintLabel] = originalFingerprint
                    }
                },
                State = new ContainerState { Status = "running", Running = true },
                NetworkSettings = new NetworkSettings
                {
                    Ports = new Dictionary<string, IList<PortBinding>>()
                }
            }),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(Client(containers));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CreateContainerAsync(original with { Command = "serve --port 9090" }));

        Assert.Contains("different container request", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryRecoverContainerAsync_ReturnsOnlyConfirmedRunningContainer()
    {
        var operationId = Guid.NewGuid();
        var startCalls = 0;
        var provider = CreateProvider(
            [Inspect("exited", running: false), Inspect("running", running: true)],
            onStart: () => startCalls++);

        var result = await provider.TryRecoverContainerAsync(
            new ContainerConfig("gamebox:latest", OperationId: operationId),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("running", result.Status);
        Assert.Equal(1, startCalls);
    }

    [Fact]
    public async Task TryRecoverContainerAsync_DeadContainer_IsRemovedForRebuild()
    {
        var removeCalls = 0;
        var provider = CreateProvider(
            [Inspect("dead", running: false, dead: true)],
            onRemove: () => removeCalls++);

        var result = await provider.TryRecoverContainerAsync(
            new ContainerConfig("gamebox:latest", OperationId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, removeCalls);
    }

    [Fact]
    public async Task TryRecoverContainerAsync_RemovingContainer_RequestsRetryWithoutMutation()
    {
        var startCalls = 0;
        var removeCalls = 0;
        var provider = CreateProvider(
            [Inspect("removing", running: false)],
            onStart: () => startCalls++,
            onRemove: () => removeCalls++);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TryRecoverContainerAsync(
                new ContainerConfig("gamebox:latest", OperationId: Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains("retry", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, startCalls);
        Assert.Equal(0, removeCalls);
    }

    [Fact]
    public async Task TryRecoverContainerAsync_StartWithoutRunning_RequestsRetry()
    {
        var provider = CreateProvider(
            [Inspect("exited", running: false), Inspect("exited", running: false)]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TryRecoverContainerAsync(
                new ContainerConfig("gamebox:latest", OperationId: Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains("did not reach running state", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DestroyContainerAsync_CleanupFailure_PropagatesForRetry()
    {
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(ManagedInspect()),
            nameof(IContainerOperations.RemoveContainerAsync) => Task.CompletedTask,
            _ => throw Unexpected(method)
        });
        var networks = Proxy<INetworkOperations>((method, _) => method.Name switch
        {
            nameof(INetworkOperations.ListNetworksAsync) => Task.FromResult<IList<NetworkResponse>>(
                [ManagedNetwork("noctf-network")]),
            nameof(INetworkOperations.DeleteNetworkAsync) => Task.FromException(
                new DockerApiException(HttpStatusCode.Conflict, "network still has endpoints")),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(Client(containers, networks));

        await Assert.ThrowsAsync<DockerApiException>(() => provider.DestroyContainerAsync(
            new DockerContainerMetadata("container-1", "", "running", [], "noctf-network")));
    }

    [Fact]
    public async Task DestroyContainerAsync_NotFoundCleanup_IsIdempotent()
    {
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromException<ContainerInspectResponse>(
                new DockerContainerNotFoundException(HttpStatusCode.NotFound, "gone")),
            _ => throw Unexpected(method)
        });
        var networks = Proxy<INetworkOperations>((method, _) => method.Name switch
        {
            nameof(INetworkOperations.ListNetworksAsync) => Task.FromResult<IList<NetworkResponse>>([]),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(Client(containers, networks));

        await provider.DestroyContainerAsync(
            new DockerContainerMetadata("container-1", "", "running", [], "noctf-network"));
    }

    [Fact]
    public async Task RemoveRunContainerAsync_CleanupFailure_PropagatesAndNotFoundIsIdempotent()
    {
        var failingContainers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.RemoveContainerAsync) => Task.FromException(
                new DockerApiException(HttpStatusCode.Conflict, "removal failed")),
            _ => throw Unexpected(method)
        });
        await Assert.ThrowsAsync<DockerApiException>(() => DockerManager.RemoveRunContainerAsync(
            Client(failingContainers),
            "one-shot"));

        var missingContainers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.RemoveContainerAsync) => Task.FromException(
                new DockerContainerNotFoundException(HttpStatusCode.NotFound, "gone")),
            _ => throw Unexpected(method)
        });
        await DockerManager.RemoveRunContainerAsync(Client(missingContainers), "one-shot");
    }

    [Fact]
    public void BoundedDockerLogCapture_LimitsRawBytesBeforeDecoding()
    {
        var capture = new BoundedDockerLogCapture();
        var oversized = Encoding.UTF8.GetBytes(new string('界', 20_000));

        capture.Append(MultiplexedStream.TargetStream.StandardOut, oversized);
        capture.Append(MultiplexedStream.TargetStream.StandardError, oversized);
        var (stdout, stderr) = capture.GetText();

        Assert.Equal(BoundedDockerLogCapture.MaxBytesPerStream, capture.StdOutBytes);
        Assert.True(capture.StdErrBytes <= BoundedDockerLogCapture.MaxBytesPerStream);
        Assert.Equal(0, capture.RemainingReadBytes);
        Assert.NotNull(stdout);
        Assert.NotNull(stderr);
    }

    [Fact]
    public async Task RunContainerAsync_RestartRecoversTerminalReceiptWithoutRerunningOrDeleting()
    {
        var operationId = Guid.NewGuid();
        var config = new ContainerConfig(
            "checker:latest",
            Command: "check",
            Ttl: TimeSpan.FromMinutes(1),
            OperationId: operationId);
        var fingerprint = DockerManager.ComputeRunFingerprint(config);
        var createCalls = 0;
        var removeCalls = 0;
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                [new ContainerListResponse { ID = "existing-run", Created = DateTime.UtcNow.AddSeconds(-5) }]),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(new ContainerInspectResponse
            {
                ID = "existing-run",
                Name = $"/noctf-run-{operationId:N}",
                Created = DateTime.UtcNow.AddSeconds(-5),
                Config = new Config
                {
                    Image = config.Image,
                    Labels = new Dictionary<string, string>
                    {
                        [DockerProvider.OperationLabel] = operationId.ToString("D"),
                        [DockerProvider.RequestFingerprintLabel] = fingerprint,
                        [DockerProvider.ManagedLabel] = bool.TrueString,
                        [DockerProvider.RunReceiptLabel] = bool.TrueString,
                        [DockerProvider.RunDeadlineLabel] = DateTimeOffset.UtcNow
                            .AddMinutes(1)
                            .ToUnixTimeMilliseconds()
                            .ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }
                },
                State = new ContainerState
                {
                    Status = "exited",
                    Running = false,
                    ExitCode = 7,
                    FinishedAt = DateTime.UtcNow.ToString("O")
                },
                NetworkSettings = new NetworkSettings
                {
                    Ports = new Dictionary<string, IList<PortBinding>>()
                }
            }),
            nameof(IContainerOperations.GetContainerLogsAsync) => throw new IOException("logs unavailable"),
            nameof(IContainerOperations.RemoveContainerAsync) => Removed(() => removeCalls++),
            nameof(IContainerOperations.CreateContainerAsync) => Created(() => createCalls++),
            _ => throw Unexpected(method)
        });
        var images = Proxy<IImageOperations>((method, _) => method.Name switch
        {
            nameof(IImageOperations.InspectImageAsync) => Task.FromResult(new ImageInspectResponse()),
            _ => throw Unexpected(method)
        });
        var manager = new DockerManager(new DockerProvider(Client(containers, images: images)));

        var result = await manager.RunContainerAsync(config);

        Assert.Equal("existing-run", result.ContainerId);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal(0, createCalls);
        Assert.Equal(0, removeCalls);
    }

    [Fact]
    public async Task RunContainerAsync_NewOperationRetainsTerminalContainerAndReceiptLabels()
    {
        var operationId = Guid.NewGuid();
        CreateContainerParameters? capturedCreate = null;
        var removeCalls = 0;
        var inspectCalls = 0;
        var containers = Proxy<IContainerOperations>((method, args) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>([]),
            nameof(IContainerOperations.CreateContainerAsync) => CaptureCreate(args, value => capturedCreate = value),
            nameof(IContainerOperations.StartContainerAsync) => Task.FromResult(true),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(
                Interlocked.Increment(ref inspectCalls) == 1
                    ? Inspect("running", running: true)
                    : Inspect("exited", running: false)),
            nameof(IContainerOperations.WaitContainerAsync) => Task.FromResult(new ContainerWaitResponse
            {
                StatusCode = 0
            }),
            nameof(IContainerOperations.GetContainerLogsAsync) => throw new IOException("logs unavailable"),
            nameof(IContainerOperations.RemoveContainerAsync) => Removed(() => removeCalls++),
            _ => throw Unexpected(method)
        });
        var images = Proxy<IImageOperations>((method, _) => method.Name switch
        {
            nameof(IImageOperations.InspectImageAsync) => Task.FromResult(new ImageInspectResponse()),
            _ => throw Unexpected(method)
        });
        var manager = new DockerManager(new DockerProvider(Client(containers, images: images)));

        var result = await manager.RunContainerAsync(new ContainerConfig(
            "checker:latest",
            Command: "check",
            Ttl: TimeSpan.FromMinutes(1),
            OperationId: operationId));

        Assert.Equal("created", result.ContainerId);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, removeCalls);
        Assert.NotNull(capturedCreate);
        Assert.Equal(bool.TrueString, capturedCreate.Labels[DockerProvider.RunReceiptLabel]);
        Assert.Equal(operationId.ToString("D"), capturedCreate.Labels[DockerProvider.OperationLabel]);
        Assert.True(capturedCreate.Labels.ContainsKey(DockerProvider.RunDeadlineLabel));
    }

    [Fact]
    public async Task RunContainerAsync_AmbiguousStartCancellationPreservesOperationContainerForRetry()
    {
        var removeCalls = 0;
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>([]),
            nameof(IContainerOperations.CreateContainerAsync) => Task.FromResult(
                new CreateContainerResponse { ID = "ambiguous-run" }),
            nameof(IContainerOperations.StartContainerAsync) => Task.FromException<bool>(
                new OperationCanceledException("start response was lost")),
            nameof(IContainerOperations.RemoveContainerAsync) => Removed(() => removeCalls++),
            _ => throw Unexpected(method)
        });
        var images = Proxy<IImageOperations>((method, _) => method.Name switch
        {
            nameof(IImageOperations.InspectImageAsync) => Task.FromResult(new ImageInspectResponse()),
            _ => throw Unexpected(method)
        });
        var manager = new DockerManager(new DockerProvider(Client(containers, images: images)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.RunContainerAsync(
            new ContainerConfig(
                "checker:latest",
                Ttl: TimeSpan.FromMinutes(1),
                OperationId: Guid.NewGuid())));

        Assert.Equal(0, removeCalls);
    }

    [Fact]
    public async Task CreateContainerAsync_NewContainerMustBeConfirmedRunning()
    {
        var removeCalls = 0;
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.CreateContainerAsync) => Task.FromResult(
                new CreateContainerResponse { ID = "new-container" }),
            nameof(IContainerOperations.StartContainerAsync) => Task.FromResult(true),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(
                Inspect("created", running: false)),
            nameof(IContainerOperations.RemoveContainerAsync) => Removed(() => removeCalls++),
            _ => throw Unexpected(method)
        });
        var images = Proxy<IImageOperations>((method, _) => method.Name switch
        {
            nameof(IImageOperations.InspectImageAsync) => Task.FromResult(new ImageInspectResponse()),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(Client(containers, images: images));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CreateContainerAsync(new ContainerConfig(
                "gamebox:latest",
                NetworkName: "shared-network")));

        Assert.Contains("did not reach running state", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, removeCalls);
    }

    [Fact]
    public async Task CleanupExpiredRunReceipts_KillsOverdueRunsAndRemovesOnlyExpiredTerminalReceipts()
    {
        var now = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var removed = new List<string>();
        var killed = new List<string>();
        var untrusted = RunReceiptInspect(
            now,
            "exited",
            running: false,
            deadline: now.AddHours(-1),
            finishedAt: now.AddHours(-1));
        untrusted.Name = "/compose-project-service-1";
        var inspections = new Dictionary<string, ContainerInspectResponse>
        {
            ["expired"] = RunReceiptInspect(
                now,
                "exited",
                running: false,
                deadline: now.AddHours(-1),
                finishedAt: now.AddMinutes(-31)),
            ["fresh"] = RunReceiptInspect(
                now,
                "exited",
                running: false,
                deadline: now.AddMinutes(-2),
                finishedAt: now.AddMinutes(-1)),
            ["overdue"] = RunReceiptInspect(
                now,
                "running",
                running: true,
                deadline: now.AddSeconds(-1),
                finishedAt: null),
            ["untrusted"] = untrusted
        };
        var containers = Proxy<IContainerOperations>((method, args) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                inspections.Keys.Select(id => new ContainerListResponse { ID = id }).ToList()),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(
                inspections[(string)args![0]!]),
            nameof(IContainerOperations.RemoveContainerAsync) => RecordId(args, removed),
            nameof(IContainerOperations.KillContainerAsync) => RecordId(args, killed),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(
            Client(containers, EmptyNetworks()),
            runReceiptRetention: TimeSpan.FromMinutes(30),
            timeProvider: new FixedTimeProvider(now));

        await provider.CleanupExpiredRunReceiptsAsync();

        Assert.Equal(["expired"], removed);
        Assert.Equal(["overdue"], killed);
    }

    [Fact]
    public async Task CreateContainerAsync_TtlUsesSeparateOrphanDeadlineAndNotOneShotReceipt()
    {
        var now = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var competitionId = Guid.NewGuid();
        CreateContainerParameters? captured = null;
        var containers = Proxy<IContainerOperations>((method, args) => method.Name switch
        {
            nameof(IContainerOperations.CreateContainerAsync) => CaptureCreate(
                args,
                value => captured = value),
            nameof(IContainerOperations.StartContainerAsync) => Task.FromResult(true),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(
                Inspect("running", running: true)),
            _ => throw Unexpected(method)
        });
        var images = Proxy<IImageOperations>((method, _) => method.Name switch
        {
            nameof(IImageOperations.InspectImageAsync) => Task.FromResult(new ImageInspectResponse()),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(
            Client(containers, images: images),
            runtimeCleanupGrace: TimeSpan.FromDays(7),
            timeProvider: new FixedTimeProvider(now));

        await provider.CreateContainerAsync(new ContainerConfig(
            "gamebox:latest",
            Labels: new Dictionary<string, string>
            {
                ["competitionId"] = competitionId.ToString("D")
            },
            NetworkName: "shared-runtime-network",
            Ttl: TimeSpan.FromHours(2)));

        Assert.NotNull(captured);
        Assert.Equal(bool.TrueString, captured.Labels[DockerProvider.RuntimeContainerLabel]);
        Assert.False(captured.Labels.ContainsKey(DockerProvider.RunReceiptLabel));
        var deadline = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(
            captured.Labels[DockerProvider.RuntimeExpiryLabel],
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(now.AddDays(7).AddHours(2), deadline);
    }

    [Fact]
    public async Task CreateContainerAsync_OwnedNetworkCarriesSameDurableOrphanMetadata()
    {
        var now = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var competitionId = Guid.NewGuid();
        CreateContainerParameters? captured = null;
        var containers = Proxy<IContainerOperations>((method, args) => method.Name switch
        {
            nameof(IContainerOperations.CreateContainerAsync) => CaptureCreate(
                args,
                value => captured = value),
            nameof(IContainerOperations.StartContainerAsync) => Task.FromResult(true),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(
                Inspect("running", running: true)),
            _ => throw Unexpected(method)
        });
        var images = Proxy<IImageOperations>((method, _) => method.Name switch
        {
            nameof(IImageOperations.InspectImageAsync) => Task.FromResult(new ImageInspectResponse()),
            _ => throw Unexpected(method)
        });
        var networks = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal);
        var provider = new DockerProvider(
            Client(containers, StatefulNetworks(networks), images),
            runtimeCleanupGrace: TimeSpan.FromDays(7),
            timeProvider: new FixedTimeProvider(now));

        var metadata = await provider.CreateContainerAsync(new ContainerConfig(
            "gamebox:latest",
            Labels: new Dictionary<string, string>
            {
                ["competitionId"] = competitionId.ToString("D")
            },
            Ttl: TimeSpan.FromHours(2)));

        Assert.NotNull(captured);
        Assert.NotNull(metadata.NetworkName);
        var network = Assert.Single(networks).Value;
        Assert.Equal(metadata.NetworkName, network.Name);
        Assert.Equal(network.Name, network.Labels[DockerProvider.OwnedNetworkLabel]);
        Assert.Equal(
            captured.Labels[DockerProvider.RuntimeExpiryLabel],
            network.Labels[DockerProvider.RuntimeExpiryLabel]);
        Assert.Equal(bool.TrueString, network.Labels[DockerProvider.RuntimeContainerLabel]);
    }

    [Fact]
    public async Task CleanupExpiredRuntime_RemovesContainerAndItsDurableOwnedNetwork()
    {
        var now = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var competitionId = Guid.NewGuid();
        const string networkName = "noctf-expired-owned-network";
        var labels = new Dictionary<string, string>
        {
            [DockerProvider.ManagedLabel] = bool.TrueString,
            [DockerProvider.RuntimeContainerLabel] = bool.TrueString,
            [DockerProvider.RuntimeExpiryLabel] = now.AddMinutes(-1)
                .ToUnixTimeMilliseconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            [DockerProvider.OwnedNetworkLabel] = networkName,
            ["competitionId"] = competitionId.ToString("D")
        };
        var removedContainers = new List<string>();
        var containers = Proxy<IContainerOperations>((method, args) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                HasLabelFilter<ContainersListParameters>(args, DockerProvider.RuntimeContainerLabel)
                    ? [new ContainerListResponse { ID = "expired-runtime", Labels = labels }]
                    : []),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(new ContainerInspectResponse
            {
                ID = "expired-runtime",
                Config = new Config { Image = "gamebox:latest", Labels = labels },
                State = new ContainerState { Running = true, Status = "running" },
                NetworkSettings = new NetworkSettings()
            }),
            nameof(IContainerOperations.RemoveContainerAsync) => RecordId(args, removedContainers),
            _ => throw Unexpected(method)
        });
        var networkState = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal)
        {
            [networkName] = new NetworkResponse
            {
                ID = "expired-network-id",
                Name = networkName,
                Labels = labels
            }
        };
        var provider = new DockerProvider(
            Client(containers, StatefulNetworks(networkState)),
            timeProvider: new FixedTimeProvider(now));

        await provider.CleanupExpiredRunReceiptsAsync();

        Assert.Equal(["expired-runtime"], removedContainers);
        Assert.DoesNotContain(networkName, networkState.Keys);
    }

    [Fact]
    public async Task CleanupExpiredComposeProject_UsesDurableClaimWithoutDatabaseMetadata()
    {
        var now = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var competitionId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        const string projectName = "noctf-expired-compose";
        var runtimeLabels = new Dictionary<string, string>
        {
            [DockerProvider.ManagedLabel] = bool.TrueString,
            [DockerProvider.ComposeResourceLabel] = bool.TrueString,
            [DockerProvider.ComposeProjectLabel] = projectName,
            [DockerProvider.OperationLabel] = operationId.ToString("D"),
            [DockerProvider.RequestFingerprintLabel] = new string('f', 64),
            [DockerProvider.RuntimeExpiryLabel] = now.AddMinutes(-1)
                .ToUnixTimeMilliseconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["competitionId"] = competitionId.ToString("D")
        };
        var claimLabels = new Dictionary<string, string>(runtimeLabels)
        {
            [DockerProvider.ComposeClaimLabel] = bool.TrueString
        };
        var projectNetworkLabels = new Dictionary<string, string>(runtimeLabels)
        {
            ["com.docker.compose.project"] = projectName
        };
        var networkState = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal)
        {
            [DockerProvider.ComposeClaimName(projectName)] = new NetworkResponse
            {
                ID = "expired-claim",
                Name = DockerProvider.ComposeClaimName(projectName),
                Labels = claimLabels
            },
            [$"{projectName}_default"] = new NetworkResponse
            {
                ID = "project-network",
                Name = $"{projectName}_default",
                Labels = projectNetworkLabels
            }
        };
        var removedContainers = new List<string>();
        var containers = Proxy<IContainerOperations>((method, args) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                HasLabelFilter<ContainersListParameters>(args, DockerProvider.RunReceiptLabel) ||
                HasLabelFilter<ContainersListParameters>(args, DockerProvider.RuntimeContainerLabel)
                    ? []
                    : [new ContainerListResponse { ID = "compose-service", Labels = runtimeLabels }]),
            nameof(IContainerOperations.RemoveContainerAsync) => RecordId(args, removedContainers),
            _ => throw Unexpected(method)
        });
        var volumes = Proxy<IVolumeOperations>((method, _) => method.Name switch
        {
            nameof(IVolumeOperations.ListAsync) => Task.FromResult(new VolumesListResponse
            {
                Volumes = []
            }),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(
            Client(
                containers,
                StatefulNetworks(networkState),
                volumes: volumes),
            timeProvider: new FixedTimeProvider(now));

        await provider.CleanupExpiredRunReceiptsAsync();

        Assert.Equal(["compose-service"], removedContainers);
        Assert.Empty(networkState);
    }

    [Fact]
    public async Task ComposeMutationLease_RejectsConcurrentDaemonMutationAndReleasesOwnership()
    {
        var networks = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal);
        var provider = new DockerProvider(Client(
            Proxy<IContainerOperations>((method, _) => throw Unexpected(method)),
            StatefulNetworks(networks)));
        const string projectName = "noctf-concurrent-project";

        await using (await provider.AcquireComposeMutationLeaseAsync(projectName, CancellationToken.None))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.AcquireComposeMutationLeaseAsync(projectName, CancellationToken.None));
            Assert.Contains("being modified", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (await provider.AcquireComposeMutationLeaseAsync(projectName, CancellationToken.None))
        {
            Assert.Contains(DockerProvider.ComposeMutexName(projectName), networks.Keys);
        }
        Assert.Empty(networks);
    }

    [Fact]
    public async Task ComposeClaim_RestartRecoversSameRequestAndRejectsFingerprintReuse()
    {
        var competitionId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        const string projectName = "noctf-compose-recovery";
        var labels = new Dictionary<string, string>
        {
            [DockerProvider.ManagedLabel] = bool.TrueString,
            [DockerProvider.ComposeResourceLabel] = bool.TrueString,
            [DockerProvider.ComposeProjectLabel] = projectName,
            [DockerProvider.OperationLabel] = operationId.ToString("D"),
            [DockerProvider.RequestFingerprintLabel] = new string('c', 64),
            ["competitionId"] = competitionId.ToString("D")
        };
        var networks = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal);
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) =>
                Task.FromResult<IList<ContainerListResponse>>([]),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(Client(containers, StatefulNetworks(networks)));

        var first = await provider.ClaimComposeProjectAsync(projectName, labels, CancellationToken.None);
        var recovered = await provider.ClaimComposeProjectAsync(projectName, labels, CancellationToken.None);
        var conflicting = new Dictionary<string, string>(labels)
        {
            [DockerProvider.RequestFingerprintLabel] = new string('d', 64)
        };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ClaimComposeProjectAsync(projectName, conflicting, CancellationToken.None));

        Assert.Null(first.RuntimeDeadline);
        Assert.Null(recovered.RuntimeDeadline);
        Assert.Contains("identity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(networks);
        Assert.Contains(DockerProvider.ComposeClaimName(projectName), networks.Keys);
    }

    [Fact]
    public async Task ComposeProjectLock_SerializesSameProjectAndReclaimsGate()
    {
        var projectLock = new ComposeProjectLock();
        var first = await projectLock.AcquireAsync("noctf-lock-project", CancellationToken.None);

        var secondTask = projectLock
            .AcquireAsync("noctf-lock-project", CancellationToken.None)
            .AsTask();
        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        var second = await secondTask;
        await second.DisposeAsync();

        Assert.Equal(0, projectLock.EntryCount);
    }

    [Fact]
    public async Task ComposeUpAsync_PersistsOperationFingerprintAndUsesEffectiveYaml()
    {
        var now = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var competitionId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var config = new ComposeConfig(
            "noctf-compose-durable",
            SecureComposeYaml,
            Labels: new Dictionary<string, string>
            {
                ["competitionId"] = competitionId.ToString("D")
            },
            Ttl: TimeSpan.FromHours(2),
            OperationId: operationId);
        var fingerprint = DockerManager.ComputeComposeFingerprint(config);
        var expectedLabels = new Dictionary<string, string>
        {
            [DockerProvider.ManagedLabel] = bool.TrueString,
            [DockerProvider.OperationLabel] = operationId.ToString("D"),
            [DockerProvider.RequestFingerprintLabel] = fingerprint,
            [DockerProvider.ComposeResourceLabel] = bool.TrueString,
            [DockerProvider.ComposeProjectLabel] = config.ProjectName,
            [DockerProvider.RuntimeExpiryLabel] = now.AddHours(2)
                .ToUnixTimeMilliseconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["competitionId"] = competitionId.ToString("D")
        };
        var networkState = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal);
        var composeInvoked = false;
        string? effectiveYaml = null;
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                composeInvoked
                    ? [new ContainerListResponse { ID = "service", Labels = expectedLabels }]
                    : []),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(
            Client(containers, StatefulNetworks(networkState)),
            timeProvider: new FixedTimeProvider(now));
        var manager = new DockerManager(
            provider,
            (yaml, project, arguments, _, _) =>
            {
                effectiveYaml = yaml;
                composeInvoked = true;
                Assert.Equal(config.ProjectName, project);
                Assert.Equal(["up", "-d", "--remove-orphans"], arguments);
                return Task.FromResult(string.Empty);
            });

        var deployment = await manager.ComposeUpAsync(config);

        Assert.True(composeInvoked);
        Assert.Equal(effectiveYaml, deployment.ComposeYaml);
        Assert.Contains($"{DockerProvider.OperationLabel}: {operationId:D}", effectiveYaml, StringComparison.Ordinal);
        Assert.Contains($"{DockerProvider.RequestFingerprintLabel}: {fingerprint}", effectiveYaml, StringComparison.Ordinal);
        Assert.Equal(now.AddHours(2).UtcDateTime, deployment.ExpectedStopAt);
        var claim = networkState[DockerProvider.ComposeClaimName(config.ProjectName)];
        Assert.Equal(operationId.ToString("D"), claim.Labels[DockerProvider.OperationLabel]);
        Assert.Equal(fingerprint, claim.Labels[DockerProvider.RequestFingerprintLabel]);
        Assert.DoesNotContain(DockerProvider.ComposeMutexName(config.ProjectName), networkState.Keys);
    }

    [Fact]
    public async Task ComposeDownAsync_RejectsCrossCompetitionProjectBeforeDockerComposeMutation()
    {
        var ownerCompetition = Guid.NewGuid();
        var attackerCompetition = Guid.NewGuid();
        const string projectName = "noctf-compose-owned";
        var operationId = Guid.NewGuid();
        var labels = new Dictionary<string, string>
        {
            [DockerProvider.ManagedLabel] = bool.TrueString,
            [DockerProvider.ComposeClaimLabel] = bool.TrueString,
            [DockerProvider.ComposeResourceLabel] = bool.TrueString,
            [DockerProvider.ComposeProjectLabel] = projectName,
            [DockerProvider.OperationLabel] = operationId.ToString("D"),
            [DockerProvider.RequestFingerprintLabel] = new string('b', 64),
            ["competitionId"] = ownerCompetition.ToString("D")
        };
        var networkState = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal)
        {
            [DockerProvider.ComposeClaimName(projectName)] = new NetworkResponse
            {
                ID = "claim-id",
                Name = DockerProvider.ComposeClaimName(projectName),
                Labels = labels
            }
        };
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                [new ContainerListResponse { ID = "owner-service", Labels = labels }]),
            _ => throw Unexpected(method)
        });
        var processCalls = 0;
        var manager = new DockerManager(
            new DockerProvider(Client(containers, StatefulNetworks(networkState))),
            (_, _, _, _, _) =>
            {
                processCalls++;
                return Task.FromResult(string.Empty);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ComposeDownAsync(
            new ComposeDeployment(
                Guid.NewGuid(),
                attackerCompetition,
                null,
                null,
                "docker-compose",
                projectName,
                SecureComposeYaml,
                "running",
                DateTime.UtcNow)));

        Assert.Equal(0, processCalls);
        Assert.Contains(DockerProvider.ComposeClaimName(projectName), networkState.Keys);
    }

    [Fact]
    public async Task ComposeDownValidation_RejectsOmittedTeamIdentity()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        const string projectName = "noctf-compose-team-owned";
        var labels = new Dictionary<string, string>
        {
            [DockerProvider.ManagedLabel] = bool.TrueString,
            [DockerProvider.ComposeClaimLabel] = bool.TrueString,
            [DockerProvider.ComposeResourceLabel] = bool.TrueString,
            [DockerProvider.ComposeProjectLabel] = projectName,
            [DockerProvider.OperationLabel] = Guid.NewGuid().ToString("D"),
            [DockerProvider.RequestFingerprintLabel] = new string('a', 64),
            ["competitionId"] = competitionId.ToString("D"),
            ["teamId"] = teamId.ToString("D")
        };
        var networks = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal)
        {
            [DockerProvider.ComposeClaimName(projectName)] = new NetworkResponse
            {
                ID = "claim-id",
                Name = DockerProvider.ComposeClaimName(projectName),
                Labels = labels
            }
        };
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                [new ContainerListResponse { ID = "team-service", Labels = labels }]),
            _ => throw Unexpected(method)
        });
        var provider = new DockerProvider(Client(containers, StatefulNetworks(networks)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ValidateComposeProjectForDownAsync(
                new ComposeDeployment(
                    Guid.NewGuid(),
                    competitionId,
                    null,
                    null,
                    "docker-compose",
                    projectName,
                    SecureComposeYaml,
                    "running",
                    DateTime.UtcNow),
                CancellationToken.None));

        Assert.Contains("teamId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeDownAsync_VerifiesIdentityAndRemovesDurableClaimAfterSuccess()
    {
        var competitionId = Guid.NewGuid();
        const string projectName = "noctf-compose-down";
        var labels = new Dictionary<string, string>
        {
            [DockerProvider.ManagedLabel] = bool.TrueString,
            [DockerProvider.ComposeClaimLabel] = bool.TrueString,
            [DockerProvider.ComposeResourceLabel] = bool.TrueString,
            [DockerProvider.ComposeProjectLabel] = projectName,
            [DockerProvider.OperationLabel] = Guid.NewGuid().ToString("D"),
            [DockerProvider.RequestFingerprintLabel] = new string('e', 64),
            ["competitionId"] = competitionId.ToString("D")
        };
        var networkState = new Dictionary<string, NetworkResponse>(StringComparer.Ordinal)
        {
            [DockerProvider.ComposeClaimName(projectName)] = new NetworkResponse
            {
                ID = "claim-id",
                Name = DockerProvider.ComposeClaimName(projectName),
                Labels = labels
            }
        };
        var downExecuted = false;
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                downExecuted
                    ? []
                    : [new ContainerListResponse { ID = "service", Labels = labels }]),
            _ => throw Unexpected(method)
        });
        var processCalls = 0;
        var manager = new DockerManager(
            new DockerProvider(Client(
                containers,
                StatefulNetworks(networkState),
                volumes: EmptyVolumes())),
            (_, _, arguments, _, _) =>
            {
                processCalls++;
                downExecuted = true;
                Assert.Equal(["down", "--remove-orphans", "--volumes"], arguments);
                return Task.FromResult(string.Empty);
            });

        await manager.ComposeDownAsync(new ComposeDeployment(
            Guid.NewGuid(),
            competitionId,
            null,
            null,
            "docker-compose",
            projectName,
            SecureComposeYaml,
            "running",
            DateTime.UtcNow));

        Assert.Equal(1, processCalls);
        Assert.Empty(networkState);
    }

    [Fact]
    public void WithManagedLabels_RemovesCallerControlledRuntimeIdentityLabels()
    {
        var operationId = Guid.NewGuid();
        var labels = DockerProvider.WithManagedLabels(
            new Dictionary<string, string>
            {
                [DockerProvider.ManagedLabel] = bool.FalseString,
                [DockerProvider.OperationLabel] = Guid.NewGuid().ToString("D"),
                [DockerProvider.RequestFingerprintLabel] = "forged",
                [DockerProvider.RunReceiptLabel] = bool.TrueString,
                [DockerProvider.RunDeadlineLabel] = "0",
                [DockerProvider.RuntimeExpiryLabel] = "0",
                [DockerProvider.ComposeClaimLabel] = bool.TrueString,
                ["competitionId"] = "competition"
            },
            operationId);

        Assert.Equal(bool.TrueString, labels[DockerProvider.ManagedLabel]);
        Assert.Equal(operationId.ToString("D"), labels[DockerProvider.OperationLabel]);
        Assert.False(labels.ContainsKey(DockerProvider.RequestFingerprintLabel));
        Assert.False(labels.ContainsKey(DockerProvider.RunReceiptLabel));
        Assert.False(labels.ContainsKey(DockerProvider.RunDeadlineLabel));
        Assert.False(labels.ContainsKey(DockerProvider.RuntimeExpiryLabel));
        Assert.False(labels.ContainsKey(DockerProvider.ComposeClaimLabel));
        Assert.Equal("competition", labels["competitionId"]);
    }

    [Fact]
    public void DockerProvider_UsesConfiguredPublishedHost()
    {
        var provider = new DockerProvider(
            Client(Proxy<IContainerOperations>((method, _) => throw Unexpected(method))),
            "docker-host.internal");

        Assert.Equal("docker-host.internal", provider.PublishedHost);
    }

    private static DockerProvider CreateProvider(
        IReadOnlyCollection<ContainerInspectResponse> inspections,
        Action? onStart = null,
        Action? onRemove = null)
    {
        var queue = new Queue<ContainerInspectResponse>(inspections);
        var containers = Proxy<IContainerOperations>((method, _) => method.Name switch
        {
            nameof(IContainerOperations.ListContainersAsync) => Task.FromResult<IList<ContainerListResponse>>(
                [new ContainerListResponse { ID = "container-1", Created = DateTime.UtcNow }]),
            nameof(IContainerOperations.InspectContainerAsync) => Task.FromResult(queue.Dequeue()),
            nameof(IContainerOperations.StartContainerAsync) => Started(onStart),
            nameof(IContainerOperations.RemoveContainerAsync) => Removed(onRemove),
            _ => throw Unexpected(method)
        });
        return new DockerProvider(Client(containers));
    }

    private static Task<bool> Started(Action? callback)
    {
        callback?.Invoke();
        return Task.FromResult(true);
    }

    private static Task Removed(Action? callback)
    {
        callback?.Invoke();
        return Task.CompletedTask;
    }

    private static Task<CreateContainerResponse> Created(Action? callback)
    {
        callback?.Invoke();
        return Task.FromResult(new CreateContainerResponse { ID = "created" });
    }

    private static Task<CreateContainerResponse> CaptureCreate(
        object?[]? args,
        Action<CreateContainerParameters> capture)
    {
        capture((CreateContainerParameters)args![0]!);
        return Task.FromResult(new CreateContainerResponse { ID = "created" });
    }

    private static Task RecordId(object?[]? args, ICollection<string> destination)
    {
        destination.Add((string)args![0]!);
        return Task.CompletedTask;
    }

    private static ContainerInspectResponse Inspect(string status, bool running, bool dead = false)
        => new()
        {
            Config = new Config
            {
                Image = "gamebox:latest",
                Labels = new Dictionary<string, string>()
            },
            State = new ContainerState { Status = status, Running = running, Dead = dead },
            NetworkSettings = new NetworkSettings
            {
                Ports = new Dictionary<string, IList<PortBinding>>()
            }
        };

    private static ContainerInspectResponse ManagedInspect()
        => new()
        {
            Config = new Config
            {
                Image = "gamebox:latest",
                Labels = new Dictionary<string, string>
                {
                    [DockerProvider.ManagedLabel] = bool.TrueString,
                    ["competitionId"] = Guid.NewGuid().ToString("D")
                }
            },
            State = new ContainerState { Status = "running", Running = true },
            NetworkSettings = new NetworkSettings
            {
                Ports = new Dictionary<string, IList<PortBinding>>()
            }
        };

    private static NetworkResponse ManagedNetwork(string name)
        => new()
        {
            ID = $"{name}-id",
            Name = name,
            Labels = new Dictionary<string, string>
            {
                [DockerProvider.ManagedLabel] = bool.TrueString
            }
        };

    private static ContainerInspectResponse RunReceiptInspect(
        DateTimeOffset createdAt,
        string status,
        bool running,
        DateTimeOffset deadline,
        DateTimeOffset? finishedAt)
    {
        var operationId = Guid.NewGuid();
        return new ContainerInspectResponse
        {
            ID = status,
            Name = $"/noctf-run-{operationId:N}",
            Created = createdAt.UtcDateTime,
            Config = new Config
            {
                Image = "checker:latest",
                Labels = new Dictionary<string, string>
                {
                    [DockerProvider.ManagedLabel] = bool.TrueString,
                    [DockerProvider.OperationLabel] = operationId.ToString("D"),
                    [DockerProvider.RequestFingerprintLabel] = "fingerprint",
                    [DockerProvider.RunReceiptLabel] = bool.TrueString,
                    [DockerProvider.RunDeadlineLabel] = deadline
                        .ToUnixTimeMilliseconds()
                        .ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            },
            State = new ContainerState
            {
                Status = status,
                Running = running,
                FinishedAt = finishedAt?.UtcDateTime.ToString("O")
            },
            NetworkSettings = new NetworkSettings
            {
                Ports = new Dictionary<string, IList<PortBinding>>()
            }
        };
    }

    private static bool HasLabelFilter<TParameters>(object?[]? args, string label)
        => args?[0] switch
        {
            ContainersListParameters parameters =>
                parameters.Filters?.TryGetValue("label", out var filters) == true &&
                filters.Keys.Any(value =>
                    value.Equals(label, StringComparison.Ordinal) ||
                    value.StartsWith($"{label}=", StringComparison.Ordinal)),
            _ => false
        };

    private static INetworkOperations EmptyNetworks()
        => StatefulNetworks(new Dictionary<string, NetworkResponse>(StringComparer.Ordinal));

    private static IVolumeOperations EmptyVolumes()
        => Proxy<IVolumeOperations>((method, _) => method.Name switch
        {
            nameof(IVolumeOperations.ListAsync) => Task.FromResult(new VolumesListResponse
            {
                Volumes = []
            }),
            _ => throw Unexpected(method)
        });

    private static INetworkOperations StatefulNetworks(
        IDictionary<string, NetworkResponse> networks)
        => Proxy<INetworkOperations>((method, args) => method.Name switch
        {
            nameof(INetworkOperations.ListNetworksAsync) => Task.FromResult<IList<NetworkResponse>>(
                networks.Values.Where(network => MatchesNetworkFilters(
                    network,
                    (NetworksListParameters)args![0]!)).ToList()),
            nameof(INetworkOperations.CreateNetworkAsync) => CreateNetwork(args, networks),
            nameof(INetworkOperations.DeleteNetworkAsync) => DeleteNetwork(args, networks),
            _ => throw Unexpected(method)
        });

    private static Task<NetworksCreateResponse> CreateNetwork(
        object?[]? args,
        IDictionary<string, NetworkResponse> networks)
    {
        var parameters = (NetworksCreateParameters)args![0]!;
        if (networks.ContainsKey(parameters.Name))
        {
            return Task.FromException<NetworksCreateResponse>(new DockerApiException(
                HttpStatusCode.Conflict,
                "network already exists"));
        }

        var id = $"{parameters.Name}-id";
        networks[parameters.Name] = new NetworkResponse
        {
            ID = id,
            Name = parameters.Name,
            Labels = parameters.Labels is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(parameters.Labels)
        };
        return Task.FromResult(new NetworksCreateResponse { ID = id });
    }

    private static Task DeleteNetwork(
        object?[]? args,
        IDictionary<string, NetworkResponse> networks)
    {
        var identifier = (string)args![0]!;
        var match = networks.FirstOrDefault(pair =>
            string.Equals(pair.Key, identifier, StringComparison.Ordinal) ||
            string.Equals(pair.Value.ID, identifier, StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(match.Key))
            networks.Remove(match.Key);
        return Task.CompletedTask;
    }

    private static bool MatchesNetworkFilters(
        NetworkResponse network,
        NetworksListParameters parameters)
    {
        if (parameters.Filters is null)
            return true;
        if (parameters.Filters.TryGetValue("name", out var names) &&
            !names.Keys.Any(name => string.Equals(name, network.Name, StringComparison.Ordinal)))
        {
            return false;
        }
        if (!parameters.Filters.TryGetValue("label", out var labels))
            return true;

        foreach (var filter in labels.Keys)
        {
            var separator = filter.IndexOf('=', StringComparison.Ordinal);
            var key = separator < 0 ? filter : filter[..separator];
            if (network.Labels?.TryGetValue(key, out var actual) != true)
                return false;
            if (separator >= 0 &&
                !string.Equals(actual, filter[(separator + 1)..], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private const string SecureComposeYaml = """
services:
  web:
    image: registry/challenge:latest
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "1000:1000"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "0.50"
          memory: "256M"
""";

    private static IDockerClient Client(
        IContainerOperations containers,
        INetworkOperations? networks = null,
        IImageOperations? images = null,
        IVolumeOperations? volumes = null)
        => Proxy<IDockerClient>((method, _) => method.Name switch
        {
            "get_Containers" => containers,
            "get_Networks" => networks ?? Proxy<INetworkOperations>((inner, _) => throw Unexpected(inner)),
            "get_Images" => images ?? Proxy<IImageOperations>((inner, _) => throw Unexpected(inner)),
            "get_Volumes" => volumes ?? Proxy<IVolumeOperations>((inner, _) => throw Unexpected(inner)),
            nameof(IDisposable.Dispose) => null,
            _ => throw Unexpected(method)
        });

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, DelegateProxy<T>>();
        ((DelegateProxy<T>)(object)proxy).Handler = handler;
        return proxy;
    }

    private static InvalidOperationException Unexpected(MethodInfo method)
        => new($"Unexpected Docker API call: {method.Name}");

    public class DelegateProxy<T> : DispatchProxy
        where T : class
    {
        public required Func<MethodInfo, object?[]?, object?> Handler { private get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod ?? throw new InvalidOperationException("Missing proxy method."), args);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
