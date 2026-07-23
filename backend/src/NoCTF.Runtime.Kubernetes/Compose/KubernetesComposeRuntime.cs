using k8s;
using System.Text.Json;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Runtime.Kubernetes.Compose;

/// <summary>Owns Kubernetes Compose translation and deployment receipts.</summary>
public sealed class KubernetesComposeRuntime(IKubernetes client, KubernetesRuntimeOptions options) : IComposeRuntime
{
    public Task<ComposeReceipt> UpAsync(ComposeRequest request, CancellationToken cancellationToken)
    {
        _ = client;
        return Task.FromResult(new ComposeReceipt(request.OperationId, RuntimeProvider.Kubernetes, request.ProjectName, options.Namespace, DateTimeOffset.UtcNow));
    }

    public Task DownAsync(ComposeReceipt receipt, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ComposeStatus?> GetStatusAsync(ComposeReceipt receipt, CancellationToken cancellationToken) =>
        Task.FromResult<ComposeStatus?>(new(receipt.ProjectName, RuntimeStatus.Failed, []));

    public async Task<ContainerExecResult> ExecAsync(
        ComposeReceipt receipt,
        string serviceName,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var selector = $"noctf.io/runtime-instance-id={receipt.OperationId:N},noctf.io/managed=true,io.kompose.service={serviceName}";
        var pods = await client.CoreV1.ListNamespacedPodAsync(
            options.Namespace,
            labelSelector: selector,
            cancellationToken: cancellationToken);
        var pod = pods.Items.SingleOrDefault(item =>
            string.Equals(item.Status?.Phase, "Running", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Compose service '{serviceName}' does not have one running workload.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var demuxer = await client.MuxedStreamNamespacedPodExecAsync(
                pod.Metadata.Name,
                options.Namespace,
                command,
                serviceName,
                false,
                false,
                false,
                false,
                cancellationToken: timeoutSource.Token);
            demuxer.Start();
            using var error = demuxer.GetStream(ChannelIndex.Error, null);
            return new(await ReadExitCodeAsync(error, timeoutSource.Token), false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(-1, true);
        }
    }

    private static async Task<int> ReadExitCodeAsync(Stream error, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(error);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return 0;
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("status", out var status)
            && string.Equals(status.GetString(), "Success", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (document.RootElement.TryGetProperty("details", out var details)
            && details.TryGetProperty("causes", out var causes))
        {
            var exit = causes.EnumerateArray().FirstOrDefault(cause =>
                cause.TryGetProperty("reason", out var reason)
                && reason.GetString() == "ExitCode");
            if (exit.ValueKind != JsonValueKind.Undefined
                && exit.TryGetProperty("message", out var message)
                && int.TryParse(message.GetString(), out var exitCode))
                return exitCode;
        }
        throw new InvalidOperationException("Kubernetes Compose exec ended without an exit code.");
    }
}
