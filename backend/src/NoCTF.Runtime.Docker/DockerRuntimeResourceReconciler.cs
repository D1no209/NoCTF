using System.Globalization;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Runtime.Docker;

public sealed class DockerRuntimeResourceReconciler(
    DockerRuntimeOptions options,
    DockerComposeRuntime compose) : IRuntimeManagedResourceReconciler, IDisposable
{
    private readonly DockerClient client = new DockerClientBuilder()
        .WithEndpoint(new Uri(options.Endpoint))
        .Build();

    public RuntimeProvider Provider => RuntimeProvider.Docker;

    public async Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
        CancellationToken cancellationToken)
    {
        var identities = new HashSet<RuntimeResourceIdentity>();
        var filters = PersistentRuntimeFilters();
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true, Filters = filters },
            cancellationToken);
        foreach (var container in containers)
        {
            if (TryReadIdentity(container.Labels, out var identity))
                identities.Add(identity);
        }

        var networks = await client.Networks.ListNetworksAsync(
            new NetworksListParameters { Filters = filters },
            cancellationToken);
        foreach (var network in networks)
        {
            if (TryReadIdentity(network.Labels, out var identity))
                identities.Add(identity);
        }

        identities.UnionWith(await compose.ListManagedAsync(cancellationToken));
        return identities.ToArray();
    }

    public async Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken)
    {
        if (identity.RuntimeInstanceId == Guid.Empty || identity.Generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(identity));
        Exception? failure = null;
        try
        {
            await compose.DestroyByIdentityAsync(identity, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var filters = IdentityFilters(identity);
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true, Filters = filters },
            cancellationToken);
        foreach (var container in containers.Where(container =>
                     TryReadIdentity(container.Labels, out var actual)
                     && actual == identity))
        {
            try
            {
                await client.Containers.RemoveContainerAsync(
                    container.ID,
                    new ContainerRemoveParameters { Force = true },
                    cancellationToken);
            }
            catch (DockerContainerNotFoundException)
            {
                // Exact cleanup is idempotent.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        var networks = await client.Networks.ListNetworksAsync(
            new NetworksListParameters { Filters = filters },
            cancellationToken);
        foreach (var network in networks.Where(network =>
                     TryReadIdentity(network.Labels, out var actual)
                     && actual == identity))
        {
            try
            {
                await client.Networks.DeleteNetworkAsync(network.ID, cancellationToken);
            }
            catch (DockerApiException exception)
                when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                // Exact cleanup is idempotent.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose() => client.Dispose();

    private static Dictionary<string, IDictionary<string, bool>> PersistentRuntimeFilters() =>
        new()
        {
            ["label"] = new Dictionary<string, bool>
            {
                ["noctf.io/managed=true"] = true,
                ["noctf.io/job-kind=persistent-runtime"] = true
            }
        };

    private static Dictionary<string, IDictionary<string, bool>> IdentityFilters(
        RuntimeResourceIdentity identity) =>
        new()
        {
            ["label"] = new Dictionary<string, bool>
            {
                ["noctf.io/managed=true"] = true,
                ["noctf.io/job-kind=persistent-runtime"] = true,
                [$"noctf.io/runtime-instance-id={identity.RuntimeInstanceId:D}"] = true,
                [$"noctf.io/generation={identity.Generation.ToString(CultureInfo.InvariantCulture)}"] =
                    true
            }
        };

    private static bool TryReadIdentity(
        IDictionary<string, string>? labels,
        out RuntimeResourceIdentity identity)
    {
        identity = default;
        if (labels is null
            || !labels.TryGetValue("noctf.io/managed", out var managed)
            || !string.Equals(managed, "true", StringComparison.Ordinal)
            || !labels.TryGetValue("noctf.io/job-kind", out var jobKind)
            || !string.Equals(jobKind, "persistent-runtime", StringComparison.Ordinal)
            || !labels.TryGetValue("noctf.io/runtime-instance-id", out var runtimeText)
            || !Guid.TryParse(runtimeText, out var runtimeId)
            || runtimeId == Guid.Empty
            || !labels.TryGetValue("noctf.io/generation", out var generationText)
            || !int.TryParse(
                generationText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var generation)
            || generation <= 0)
            return false;
        identity = new(runtimeId, generation);
        return true;
    }
}
