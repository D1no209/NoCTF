using System.Globalization;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Messages;

public sealed record ExpandedRuntimeUrls(
    IReadOnlyList<string> Urls,
    IReadOnlyList<int> ParticipantUrlIndexes,
    string? ControlCheckUrl);

public static class RuntimeUrlExpander
{
    public static ExpandedRuntimeUrls ExpandContainer(
        ContainerReceipt receipt,
        IReadOnlyList<RuntimeUrlBinding>? bindings,
        RuntimeUrlBinding? controlCheckBinding)
    {
        var urls = new List<string>();
        var participantIndexes = new List<int>();
        foreach (var binding in bindings ?? [])
        {
            var url = ExpandPublicContainerBinding(receipt, binding);
            if (binding.Exposure == RuntimeExposure.Participants)
                participantIndexes.Add(urls.Count);
            urls.Add(url);
        }

        var controlCheckUrl = controlCheckBinding is null
            ? null
            : ExpandInternalContainerBinding(receipt, controlCheckBinding);
        return new(urls, participantIndexes, controlCheckUrl);
    }

    public static ExpandedRuntimeUrls ExpandCompose(
        ComposeReceipt receipt,
        ComposeStatus status,
        IReadOnlyList<RuntimeUrlBinding>? bindings,
        RuntimeUrlBinding? controlCheckBinding)
    {
        var urls = new List<string>();
        var participantIndexes = new List<int>();
        foreach (var binding in bindings ?? [])
        {
            var service = FindComposeService(status, binding);
            var containerPort = binding.ContainerPort
                ?? throw new InvalidOperationException(
                    "Compose URL binding requires ContainerPort.");
            if (!service.PublishedPorts.TryGetValue(containerPort, out var publicPort)
                || publicPort is < 1 or > 65535)
                throw new InvalidOperationException(
                    "Compose URL binding has no dynamic public port.");
            var url = Expand(binding.UrlTemplate, receipt.PublicHost, publicPort);
            if (binding.Exposure == RuntimeExposure.Participants)
                participantIndexes.Add(urls.Count);
            urls.Add(url);
        }

        string? controlCheckUrl = null;
        if (controlCheckBinding is not null)
        {
            var service = FindComposeService(status, controlCheckBinding);
            var containerPort = controlCheckBinding.ContainerPort
                ?? throw new InvalidOperationException(
                    "Compose control URL binding requires ContainerPort.");
            controlCheckUrl = Expand(
                controlCheckBinding.UrlTemplate,
                service.InternalHost,
                containerPort);
        }
        return new(urls, participantIndexes, controlCheckUrl);
    }

    private static ComposeServiceStatus FindComposeService(
        ComposeStatus status,
        RuntimeUrlBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.ServiceName))
            throw new InvalidOperationException(
                "Compose URL binding requires ServiceName.");
        return status.Services.SingleOrDefault(service =>
                   string.Equals(
                       service.Name,
                       binding.ServiceName,
                       StringComparison.Ordinal))
               ?? throw new InvalidOperationException(
                   $"Compose service '{binding.ServiceName}' was not found.");
    }

    private static string ExpandPublicContainerBinding(
        ContainerReceipt receipt,
        RuntimeUrlBinding binding)
    {
        var containerPort = binding.ContainerPort
            ?? throw new InvalidOperationException("Container URL binding requires ContainerPort.");
        if (!receipt.PortMappings.TryGetValue(containerPort, out var publicPort)
            || publicPort is < 1 or > 65535)
            throw new InvalidOperationException("Container URL binding has no dynamic public port.");
        return Expand(
            binding.UrlTemplate,
            receipt.PublicHost,
            publicPort);
    }

    private static string ExpandInternalContainerBinding(
        ContainerReceipt receipt,
        RuntimeUrlBinding binding)
    {
        var containerPort = binding.ContainerPort
            ?? throw new InvalidOperationException("Control URL binding requires ContainerPort.");
        return Expand(
            binding.UrlTemplate,
            receipt.InternalHost,
            containerPort);
    }

    private static string Expand(string template, string? host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Runtime receipt does not contain the required host.");
        var expanded = template
            .Replace("{HOST}", host, StringComparison.Ordinal)
            .Replace(
                "{PORT}",
                port.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        if (!Uri.TryCreate(expanded, UriKind.Absolute, out _))
            throw new InvalidOperationException("Runtime URL binding did not expand to an absolute URI.");
        return expanded;
    }
}
